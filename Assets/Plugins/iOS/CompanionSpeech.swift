import AVFoundation
import Foundation
import Speech

// Push-to-talk recognition. SpeechAnalyzer/SpeechTranscriber on iOS 26+, SFSpeechRecognizer before.
// Callbacks are invoked on the main thread; CompanionNative.mm forwards them to Unity.
@objc public final class CompanionSpeech: NSObject {
    @objc public static let shared = CompanionSpeech()

    public typealias TextHandler = (String) -> Void

    private let engine = AVAudioEngine()
    private var onPartial: TextHandler = { _ in }
    private var onFinal: TextHandler = { _ in }
    private var onError: TextHandler = { _ in }

    // SFSpeechRecognizer path
    private var legacyRequest: SFSpeechAudioBufferRecognitionRequest?
    private var legacyTask: SFSpeechRecognitionTask?

    // SpeechAnalyzer path (typed as Any so the class still loads on iOS < 26)
    private var analyzerSession: Any?

    @objc public func start(locale: String,
                            onPartial: @escaping TextHandler,
                            onFinal: @escaping TextHandler,
                            onError: @escaping TextHandler) {
        self.onPartial = { t in DispatchQueue.main.async { onPartial(t) } }
        self.onFinal = { t in DispatchQueue.main.async { onFinal(t) } }
        self.onError = { t in DispatchQueue.main.async { onError(t) } }

        Task {
            guard await Self.requestPermissions() else {
                self.onError("permission denied")
                return
            }
            do {
                try Self.configureAudioSession()
                if #available(iOS 26.0, *) {
                    try await self.startAnalyzer(locale: Locale(identifier: locale))
                } else {
                    try self.startLegacy(locale: Locale(identifier: locale))
                }
            } catch {
                self.teardownEngine()
                self.onError(error.localizedDescription)
            }
        }
    }

    @objc public func stop() {
        if #available(iOS 26.0, *), let session = analyzerSession as? AnalyzerSession {
            analyzerSession = nil
            teardownEngine()
            Task { await session.finish() }
            return
        }
        teardownEngine()
        legacyRequest?.endAudio()
    }

    // MARK: - Audio

    private static func configureAudioSession() throws {
        let s = AVAudioSession.sharedInstance()
        // voiceChat enables echo cancellation; the agent's TTS keeps playing through the speaker.
        try s.setCategory(.playAndRecord, mode: .voiceChat, options: [.defaultToSpeaker, .allowBluetoothHFP])
        try s.setActive(true)
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

    private func teardownEngine() {
        if engine.isRunning { engine.stop() }
        engine.inputNode.removeTap(onBus: 0)
    }

    // MARK: - iOS 26+: SpeechAnalyzer

    @available(iOS 26.0, *)
    private final class AnalyzerSession {
        let analyzer: SpeechAnalyzer
        let input: AsyncStream<AnalyzerInput>.Continuation
        let results: Task<Void, Never>

        init(analyzer: SpeechAnalyzer, input: AsyncStream<AnalyzerInput>.Continuation, results: Task<Void, Never>) {
            self.analyzer = analyzer
            self.input = input
            self.results = results
        }

        func finish() async {
            input.finish()
            try? await analyzer.finalizeAndFinishThroughEndOfInput()
            _ = await results.value
        }
    }

    @available(iOS 26.0, *)
    private func startAnalyzer(locale requested: Locale) async throws {
        guard let locale = await SpeechTranscriber.supportedLocale(equivalentTo: requested) else {
            throw NSError(domain: "CompanionSpeech", code: 1, userInfo: [NSLocalizedDescriptionKey: "unsupported locale"])
        }
        let transcriber = SpeechTranscriber(locale: locale, preset: .progressiveTranscription)
        if let install = try await AssetInventory.assetInstallationRequest(supporting: [transcriber]) {
            try await install.downloadAndInstall()
        }

        let inputFormat = engine.inputNode.outputFormat(forBus: 0)
        guard let analyzerFormat = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [transcriber], considering: inputFormat) else {
            throw NSError(domain: "CompanionSpeech", code: 2, userInfo: [NSLocalizedDescriptionKey: "no compatible audio format"])
        }
        let (stream, continuation) = AsyncStream.makeStream(of: AnalyzerInput.self)
        let analyzer = SpeechAnalyzer(modules: [transcriber])

        let onPartial = self.onPartial, onFinal = self.onFinal, onError = self.onError
        let results = Task {
            var finalized = ""
            var volatile = ""
            do {
                for try await result in transcriber.results {
                    let text = String(result.text.characters)
                    if result.isFinal { finalized += text; volatile = "" } else { volatile = text }
                    onPartial(finalized + volatile)
                }
                onFinal(finalized + volatile)
            } catch {
                onError(error.localizedDescription)
            }
        }

        let converter: AVAudioConverter? = inputFormat.isEqual(analyzerFormat) ? nil
            : AVAudioConverter(from: inputFormat, to: analyzerFormat)
        engine.inputNode.installTap(onBus: 0, bufferSize: 4096, format: nil) { buffer, _ in
            if let converter = converter {
                let ratio = analyzerFormat.sampleRate / inputFormat.sampleRate
                let capacity = AVAudioFrameCount(Double(buffer.frameLength) * ratio + 1)
                guard let out = AVAudioPCMBuffer(pcmFormat: analyzerFormat, frameCapacity: capacity) else { return }
                var err: NSError?
                _ = converter.convert(to: out, error: &err) { _, status in
                    status.pointee = .haveData
                    return buffer
                }
                if err == nil { continuation.yield(AnalyzerInput(buffer: out)) }
            } else {
                continuation.yield(AnalyzerInput(buffer: buffer))
            }
        }
        engine.prepare()
        try engine.start()
        try await analyzer.start(inputSequence: stream)
        analyzerSession = AnalyzerSession(analyzer: analyzer, input: continuation, results: results)
    }

    // MARK: - iOS < 26: SFSpeechRecognizer

    private func startLegacy(locale: Locale) throws {
        guard let recognizer = SFSpeechRecognizer(locale: locale), recognizer.isAvailable else {
            throw NSError(domain: "CompanionSpeech", code: 3, userInfo: [NSLocalizedDescriptionKey: "recognizer unavailable"])
        }
        let request = SFSpeechAudioBufferRecognitionRequest()
        request.shouldReportPartialResults = true
        if recognizer.supportsOnDeviceRecognition { request.requiresOnDeviceRecognition = true }
        legacyRequest = request

        let onPartial = self.onPartial, onFinal = self.onFinal, onError = self.onError
        legacyTask = recognizer.recognitionTask(with: request) { [weak self] result, error in
            if let result {
                let text = result.bestTranscription.formattedString
                if result.isFinal { onFinal(text); self?.legacyTask = nil } else { onPartial(text) }
            } else if let error {
                onError(error.localizedDescription)
                self?.legacyTask = nil
            }
        }

        let format = engine.inputNode.outputFormat(forBus: 0)
        engine.inputNode.installTap(onBus: 0, bufferSize: 4096, format: format) { buffer, _ in
            request.append(buffer)
        }
        engine.prepare()
        try engine.start()
    }
}
