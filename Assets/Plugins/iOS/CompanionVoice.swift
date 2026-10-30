import AVFoundation
import Foundation
import Speech

// Hands-free conversation audio, all on one AVAudioEngine so the system echo canceller (voice
// processing) can take her voice out of the microphone signal:
//  - play(pcm:) plays her streamed voice through a player node; playedSamples() reports progress
//    so Unity can keep subtitles and lip sync in step;
//  - start(locale:) listens continuously, cuts the user's speech into utterances (an utterance ends
//    after a short pause) and reports when the user starts talking, so the app can interrupt her.
// SpeechAnalyzer/SpeechTranscriber on iOS 26+, SFSpeechRecognizer before.
// Public methods are called on the main thread and callbacks are invoked on the main thread;
// CompanionNative.mm forwards them to Unity.
@objc public final class CompanionVoice: NSObject {
    @objc public static let shared = CompanionVoice()

    public typealias TextHandler = (String) -> Void

    // Pause after the last change in the transcript that ends an utterance.
    static let endOfUtterance: TimeInterval = 0.9
    // While she is talking, a few stray words the echo canceller let through shouldn't cut her off:
    // the user only interrupts once the transcript is this long.
    static let bargeInMinLength = 3

    struct Handlers {
        let speechStart: () -> Void
        let partial: TextHandler
        let final: TextHandler
        let error: TextHandler
    }

    private var engine: AVAudioEngine?
    private var player: AVAudioPlayerNode?
    private var playerFormat: AVAudioFormat?
    private var engineObserver: NSObjectProtocol?
    private var hasInput = false
    fileprivate var conversation: Conversation?
    // Bumped by start() and stop(): asynchronous setup from an abandoned start gives up.
    private var generation = 0
    private var reconfigurations: [Date] = []

    // Playback timeline, in samples of the player's format: each scheduled buffer is a segment.
    // Buffers are kept until played so they can be rescheduled after the engine restarts.
    private var segments: [(start: Int64, frames: Int64, buffer: AVAudioPCMBuffer)] = []
    private var completed: Int64 = 0
    private var queueEnd: Int64 = 0

    private override init() {
        super.init()
        NotificationCenter.default.addObserver(forName: AVAudioSession.interruptionNotification, object: nil, queue: .main) { [weak self] note in
            guard let self,
                  let raw = note.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt,
                  AVAudioSession.InterruptionType(rawValue: raw) == .began else { return }
            if self.conversation != nil { self.fail("interrupted") } else { self.teardownEngine() }
        }
    }

    // MARK: - Conversation

    @objc public func start(locale: String,
                            onSpeechStart: @escaping () -> Void,
                            onPartial: @escaping TextHandler,
                            onFinal: @escaping TextHandler,
                            onError: @escaping TextHandler) {
        stop()
        let gen = generation
        let handlers = Handlers(speechStart: onSpeechStart, partial: onPartial, final: onFinal, error: onError)
        Task { @MainActor in
            guard await Self.requestPermissions() else {
                if self.generation == gen { onError("permission denied") }
                return
            }
            guard self.generation == gen else { return }
            do {
                try await self.beginConversation(locale: Locale(identifier: locale), handlers: handlers, generation: gen)
            } catch {
                guard self.generation == gen else { return }
                self.stop()
                onError(error.localizedDescription)
            }
        }
    }

    @objc public func stop() {
        generation += 1
        guard let conversation else { return }
        self.conversation = nil
        conversation.finish()
        teardownEngine()
    }

    private func beginConversation(locale: Locale, handlers: Handlers, generation gen: Int) async throws {
        let factory = try await makeRecognizerFactory(locale: locale)
        guard generation == gen else { return }

        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.playAndRecord, mode: .voiceChat, options: [.defaultToSpeaker, .allowBluetoothHFP])
        try session.setActive(true)

        let conversation = Conversation(factory: factory, handlers: handlers, owner: self)
        let engine = try makeEngine(voiceProcessing: true)
        self.conversation = conversation // from here on, stop() tears everything down

        guard try await installInput(engine, for: conversation) else { return }
        if !engine.isRunning { try engine.start() }
        conversation.beginUtterance()
    }

    // Taps the (voice-processed) microphone for the conversation's recognizer. False if the
    // conversation or engine was replaced meanwhile.
    private func installInput(_ engine: AVAudioEngine, for conversation: Conversation) async throws -> Bool {
        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        guard format.sampleRate > 0, format.channelCount > 0 else {
            throw NSError(domain: "CompanionVoice", code: 4, userInfo: [NSLocalizedDescriptionKey: "no microphone input"])
        }
        try await conversation.factory.prepare(inputFormat: format)
        guard self.conversation === conversation, self.engine === engine else { return false }
        // A reconfiguration during setup may already have tapped the input; there can be only one tap.
        input.removeTap(onBus: 0)
        input.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak conversation] buffer, _ in
            conversation?.feed(buffer)
        }
        return true
    }

    // The engine stops itself when the audio hardware is reconfigured: right after voice processing
    // starts, and on route changes (headphones, Bluetooth). Re-tap the microphone (its format may have
    // changed), restart, and pick her voice up where it was. Give up only if it keeps happening.
    private func handleConfigurationChange(_ engine: AVAudioEngine) {
        guard self.engine === engine else { return }
        let now = Date()
        reconfigurations = reconfigurations.filter { now.timeIntervalSince($0) < 5 } + [now]
        NSLog("CompanionVoice: audio configuration changed (\(reconfigurations.count) in 5 s), restarting engine")
        guard reconfigurations.count <= 5 else {
            if conversation != nil { fail("audio route changed") } else { teardownEngine() }
            return
        }
        let gen = generation
        Task { @MainActor in
            do {
                if let conversation = self.conversation, self.hasInput {
                    engine.inputNode.removeTap(onBus: 0)
                    guard try await self.installInput(engine, for: conversation) else { return }
                }
                guard self.generation == gen, self.engine === engine else { return }
                if !engine.isRunning { try engine.start() }
                self.reschedulePending()
                self.conversation?.beginUtterance()
            } catch {
                guard self.generation == gen, self.engine === engine else { return }
                if self.conversation != nil { self.fail(error.localizedDescription) } else { self.teardownEngine() }
            }
        }
    }

    // Drops what has been heard of the current utterance (Unity decided it was her own voice) and
    // starts listening afresh, so the user's next words don't arrive glued to her echo.
    @objc public func discardUtterance() {
        conversation?.beginUtterance()
    }

    fileprivate func fail(_ message: String) {
        let handlers = conversation?.handlers
        stop()
        handlers?.error(message)
    }

    // MARK: - Playback

    @objc public func play(pcm: Data, sampleRate: Int) {
        let frames = pcm.count / 2
        guard frames > 0 else { return }
        do {
            try ensureEngine(sampleRate: Double(sampleRate))
        } catch {
            NSLog("CompanionVoice: playback failed: \(error)")
            return
        }
        guard let player, let playerFormat,
              let buffer = AVAudioPCMBuffer(pcmFormat: playerFormat, frameCapacity: AVAudioFrameCount(frames)) else { return }
        buffer.frameLength = AVAudioFrameCount(frames)
        let out = buffer.floatChannelData![0]
        pcm.withUnsafeBytes { raw in
            for i in 0..<frames {
                out[i] = Float(Int16(littleEndian: raw.loadUnaligned(fromByteOffset: i * 2, as: Int16.self))) / 32768
            }
        }
        schedule(buffer, on: player)
    }

    private func schedule(_ buffer: AVAudioPCMBuffer, on player: AVAudioPlayerNode) {
        let frames = Int64(buffer.frameLength)
        let start = max(currentSample(), queueEnd)
        segments.append((start, frames, buffer))
        queueEnd = start + frames
        player.scheduleBuffer(buffer, completionHandler: nil)
        if !player.isPlaying { player.play() }
    }

    // After a restart: replays what hadn't finished (the interrupted buffer from its start).
    // playedSamples() may step back a little; Unity only moves forward.
    private func reschedulePending() {
        guard let player, !segments.isEmpty else { return }
        let pending = segments
        player.stop()
        segments.removeAll()
        queueEnd = 0
        for segment in pending { schedule(segment.buffer, on: player) }
    }

    // Drops everything queued (she was interrupted). playedSamples() starts again from 0.
    @objc public func stopPlayback() {
        player?.stop()
        resetTimeline()
    }

    // Samples of the scheduled audio that have been played since the last stopPlayback().
    @objc public func playedSamples() -> Int64 {
        let now = currentSample()
        while let first = segments.first, now >= first.start + first.frames {
            completed += first.frames
            segments.removeFirst()
        }
        guard let first = segments.first else { return completed }
        return completed + max(0, min(now - first.start, first.frames))
    }

    fileprivate var isSpeaking: Bool {
        _ = playedSamples()
        return !segments.isEmpty
    }

    private func currentSample() -> Int64 {
        guard let player, player.isPlaying, let format = playerFormat,
              let nodeTime = player.lastRenderTime,
              let time = player.playerTime(forNodeTime: nodeTime) else { return 0 }
        return Int64(Double(time.sampleTime) * format.sampleRate / time.sampleRate)
    }

    private func resetTimeline() {
        segments.removeAll()
        completed = 0
        queueEnd = 0
    }

    // MARK: - Engine

    // Outside a conversation she is played on an output-only engine, so the microphone stays off.
    private func ensureEngine(sampleRate: Double) throws {
        if engine == nil {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.playback, mode: .spokenAudio)
            try session.setActive(true)
            _ = try makeEngine(voiceProcessing: false)
        }
        guard let engine, let player else { return }
        if playerFormat?.sampleRate != sampleRate {
            player.stop()
            resetTimeline()
            connectPlayer(engine: engine, player: player, sampleRate: sampleRate)
        }
        if !engine.isRunning { try engine.start() }
    }

    private func makeEngine(voiceProcessing: Bool) throws -> AVAudioEngine {
        teardownEngine()
        let engine = AVAudioEngine()
        if voiceProcessing {
            try engine.inputNode.setVoiceProcessingEnabled(true)
            if #available(iOS 17.0, *) {
                // Don't duck the rest of the app's audio while listening.
                engine.inputNode.voiceProcessingOtherAudioDuckingConfiguration =
                    AVAudioVoiceProcessingOtherAudioDuckingConfiguration(enableAdvancedDucking: false, duckingLevel: .min)
            }
        }
        let player = AVAudioPlayerNode()
        engine.attach(player)
        connectPlayer(engine: engine, player: player, sampleRate: playerFormat?.sampleRate ?? 24000)
        self.engine = engine
        self.player = player
        hasInput = voiceProcessing
        engineObserver = NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main) { [weak self, weak engine] _ in
            guard let self, let engine else { return }
            self.handleConfigurationChange(engine)
        }
        return engine
    }

    private func connectPlayer(engine: AVAudioEngine, player: AVAudioPlayerNode, sampleRate: Double) {
        let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: sampleRate, channels: 1, interleaved: false)!
        engine.disconnectNodeOutput(player)
        engine.connect(player, to: engine.mainMixerNode, format: format)
        playerFormat = format
    }

    private func teardownEngine() {
        if let engineObserver { NotificationCenter.default.removeObserver(engineObserver) }
        engineObserver = nil
        if let engine {
            if hasInput { engine.inputNode.removeTap(onBus: 0) }
            engine.stop()
        }
        engine = nil
        player = nil
        hasInput = false
        reconfigurations.removeAll()
        resetTimeline()
    }

    private static func requestPermissions() async -> Bool {
        let speech = await withCheckedContinuation { c in
            SFSpeechRecognizer.requestAuthorization { c.resume(returning: $0 == .authorized) }
        }
        let mic: Bool
        if #available(iOS 17.0, *) {
            mic = await AVAudioApplication.requestRecordPermission()
        } else {
            mic = await withCheckedContinuation { c in
                AVAudioSession.sharedInstance().requestRecordPermission { c.resume(returning: $0) }
            }
        }
        return speech && mic
    }

    private func makeRecognizerFactory(locale: Locale) async throws -> RecognizerFactory {
        if #available(iOS 26.0, *) {
            return try await AnalyzerFactory.make(locale: locale)
        }
        return try LegacyFactory(locale: locale)
    }
}

// MARK: - Utterances

// One listening session: feeds microphone audio to the current utterance's recognizer and decides
// when the user started and finished talking. Main thread, except feed() (audio tap thread).
fileprivate final class Conversation {
    let factory: RecognizerFactory
    let handlers: CompanionVoice.Handlers
    private weak var owner: CompanionVoice?
    private let lock = NSLock()
    private var utterance: Utterance? // guarded by lock
    private var utteranceId = 0
    private var text = ""
    private var started = false
    private var silence: DispatchWorkItem?
    private var startedAt = Date()
    private var quickFailures = 0

    init(factory: RecognizerFactory, handlers: CompanionVoice.Handlers, owner: CompanionVoice) {
        self.factory = factory
        self.handlers = handlers
        self.owner = owner
    }

    func feed(_ buffer: AVAudioPCMBuffer) {
        lock.lock()
        let current = utterance
        lock.unlock()
        current?.append(buffer)
    }

    func beginUtterance() {
        utteranceId += 1
        let id = utteranceId
        text = ""
        started = false
        silence?.cancel()
        silence = nil
        startedAt = Date()
        let next = factory.makeUtterance(
            onText: { [weak self] t in DispatchQueue.main.async { self?.onText(id, t) } },
            onEnd: { [weak self] e in DispatchQueue.main.async { self?.onEnded(id, e) } })
        lock.lock()
        let old = utterance
        utterance = next
        lock.unlock()
        old?.cancel()
    }

    func finish() {
        utteranceId += 1
        silence?.cancel()
        silence = nil
        lock.lock()
        let old = utterance
        utterance = nil
        lock.unlock()
        old?.cancel()
    }

    private var isCurrent: Bool { owner?.conversation === self }

    private func onText(_ id: Int, _ raw: String) {
        guard id == utteranceId, isCurrent else { return }
        let t = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty, t != text else { return }
        text = t
        if !started, t.count >= (owner?.isSpeaking == true ? CompanionVoice.bargeInMinLength : 1) {
            started = true
            quickFailures = 0
            handlers.speechStart()
        }
        if started { handlers.partial(t) }

        // Ends the utterance after a pause; a fragment too short to count is dropped instead.
        silence?.cancel()
        let item = DispatchWorkItem { [weak self] in self?.endUtterance(id) }
        silence = item
        DispatchQueue.main.asyncAfter(deadline: .now() + CompanionVoice.endOfUtterance, execute: item)
    }

    private func endUtterance(_ id: Int) {
        guard id == utteranceId, isCurrent else { return }
        let t = text, wasStarted = started
        beginUtterance()
        if wasStarted { handlers.final(t) }
    }

    // The recognizer stopped on its own (final result, time limit, long silence, error): start over.
    private func onEnded(_ id: Int, _ error: Error?) {
        guard id == utteranceId, isCurrent else { return }
        if started {
            endUtterance(id)
            return
        }
        if error != nil, Date().timeIntervalSince(startedAt) < 1 {
            quickFailures += 1
            if quickFailures >= 3 {
                owner?.fail(error?.localizedDescription ?? "recognition failed")
                return
            }
        }
        beginUtterance()
    }
}

fileprivate protocol Utterance: AnyObject {
    func append(_ buffer: AVAudioPCMBuffer) // audio tap thread
    func cancel()
}

fileprivate protocol RecognizerFactory: AnyObject {
    func prepare(inputFormat: AVAudioFormat) async throws
    func makeUtterance(onText: @escaping (String) -> Void, onEnd: @escaping (Error?) -> Void) -> Utterance
}

// MARK: iOS < 26: SFSpeechRecognizer, one request per utterance

fileprivate final class LegacyFactory: RecognizerFactory {
    let recognizer: SFSpeechRecognizer

    init(locale: Locale) throws {
        guard let recognizer = SFSpeechRecognizer(locale: locale), recognizer.isAvailable else {
            throw NSError(domain: "CompanionVoice", code: 3, userInfo: [NSLocalizedDescriptionKey: "recognizer unavailable"])
        }
        self.recognizer = recognizer
    }

    func prepare(inputFormat: AVAudioFormat) async throws {}

    func makeUtterance(onText: @escaping (String) -> Void, onEnd: @escaping (Error?) -> Void) -> Utterance {
        LegacyUtterance(recognizer: recognizer, onText: onText, onEnd: onEnd)
    }
}

fileprivate final class LegacyUtterance: Utterance {
    private let request = SFSpeechAudioBufferRecognitionRequest()
    private var task: SFSpeechRecognitionTask?

    init(recognizer: SFSpeechRecognizer, onText: @escaping (String) -> Void, onEnd: @escaping (Error?) -> Void) {
        request.shouldReportPartialResults = true
        if recognizer.supportsOnDeviceRecognition { request.requiresOnDeviceRecognition = true }
        task = recognizer.recognitionTask(with: request) { result, error in
            if let result {
                onText(result.bestTranscription.formattedString)
                if result.isFinal { onEnd(nil) }
            } else if let error {
                onEnd(error)
            }
        }
    }

    func append(_ buffer: AVAudioPCMBuffer) { request.append(buffer) }

    func cancel() {
        request.endAudio()
        task?.cancel()
        task = nil
    }
}

// MARK: iOS 26+: SpeechAnalyzer, one analyzer per utterance

@available(iOS 26.0, *)
fileprivate final class AnalyzerFactory: RecognizerFactory {
    let locale: Locale
    private var analyzerFormat: AVAudioFormat?
    private var converter: AVAudioConverter? // used on the audio tap thread only

    private init(locale: Locale) { self.locale = locale }

    static func make(locale requested: Locale) async throws -> AnalyzerFactory {
        guard let locale = await SpeechTranscriber.supportedLocale(equivalentTo: requested) else {
            throw NSError(domain: "CompanionVoice", code: 1, userInfo: [NSLocalizedDescriptionKey: "unsupported locale"])
        }
        if let install = try await AssetInventory.assetInstallationRequest(supporting: [Self.transcriber(locale)]) {
            try await install.downloadAndInstall()
        }
        return AnalyzerFactory(locale: locale)
    }

    static func transcriber(_ locale: Locale) -> SpeechTranscriber {
        SpeechTranscriber(locale: locale, preset: .progressiveTranscription)
    }

    func prepare(inputFormat: AVAudioFormat) async throws {
        guard let format = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [Self.transcriber(locale)], considering: inputFormat) else {
            throw NSError(domain: "CompanionVoice", code: 2, userInfo: [NSLocalizedDescriptionKey: "no compatible audio format"])
        }
        analyzerFormat = format
        converter = inputFormat.isEqual(format) ? nil : AVAudioConverter(from: inputFormat, to: format)
    }

    func makeUtterance(onText: @escaping (String) -> Void, onEnd: @escaping (Error?) -> Void) -> Utterance {
        AnalyzerUtterance(factory: self, onText: onText, onEnd: onEnd)
    }

    func convert(_ buffer: AVAudioPCMBuffer) -> AVAudioPCMBuffer? {
        guard let converter, let analyzerFormat else { return buffer }
        let capacity = AVAudioFrameCount(Double(buffer.frameLength) * analyzerFormat.sampleRate / buffer.format.sampleRate) + 16
        guard let out = AVAudioPCMBuffer(pcmFormat: analyzerFormat, frameCapacity: capacity) else { return nil }
        var supplied = false
        var error: NSError?
        converter.convert(to: out, error: &error) { _, status in
            if supplied {
                status.pointee = .noDataNow
                return nil
            }
            supplied = true
            status.pointee = .haveData
            return buffer
        }
        return error == nil ? out : nil
    }
}

@available(iOS 26.0, *)
fileprivate final class AnalyzerUtterance: Utterance {
    private let factory: AnalyzerFactory
    private let input: AsyncStream<AnalyzerInput>.Continuation
    private let analyzer: SpeechAnalyzer
    private var results: Task<Void, Never>?

    init(factory: AnalyzerFactory, onText: @escaping (String) -> Void, onEnd: @escaping (Error?) -> Void) {
        self.factory = factory
        let (stream, continuation) = AsyncStream.makeStream(of: AnalyzerInput.self)
        input = continuation
        let transcriber = AnalyzerFactory.transcriber(factory.locale)
        let analyzer = SpeechAnalyzer(modules: [transcriber])
        self.analyzer = analyzer
        results = Task {
            var finalized = ""
            var volatile = ""
            do {
                for try await result in transcriber.results {
                    let text = String(result.text.characters)
                    if result.isFinal { finalized += text; volatile = "" } else { volatile = text }
                    onText(finalized + volatile)
                }
                onEnd(nil)
            } catch {
                onEnd(error)
            }
        }
        Task {
            do { try await analyzer.start(inputSequence: stream) } catch { onEnd(error) }
        }
    }

    func append(_ buffer: AVAudioPCMBuffer) {
        if let converted = factory.convert(buffer) { input.yield(AnalyzerInput(buffer: converted)) }
    }

    func cancel() {
        input.finish()
        results?.cancel()
        results = nil
        let analyzer = self.analyzer
        Task { await analyzer.cancelAndFinishNow() }
    }
}
