import AuthenticationServices
import Foundation
import GoogleSignIn
import UIKit

// Sign in with Apple (AuthenticationServices) and Google (official GoogleSignIn SDK, added to the
// Xcode project as a Swift package by IOSPostBuild). GoogleSignIn reads GIDClientID / GIDServerClientID
// from Info.plist, so the ID token's audience is the backend's Web client ID.
// The completion receives a JSON string matching Companion.NativeAuthResult.
@objc public final class CompanionAuth: NSObject {
    @objc public static let shared = CompanionAuth()

    public typealias Completion = (String) -> Void

    private var completion: Completion?

    // MARK: Apple

    @objc public func signInWithApple(hashedNonce: String, completion: @escaping Completion) {
        self.completion = completion
        let request = ASAuthorizationAppleIDProvider().createRequest()
        request.requestedScopes = [.fullName, .email]
        request.nonce = hashedNonce
        let controller = ASAuthorizationController(authorizationRequests: [request])
        controller.delegate = self
        controller.presentationContextProvider = self
        controller.performRequests()
    }

    // MARK: Google

    @objc public func signInWithGoogle(completion: @escaping Completion) {
        self.completion = completion
        guard let presenter = Self.topViewController() else {
            return finish(provider: "google", error: "no view controller to present from")
        }
        GIDSignIn.sharedInstance.signIn(withPresenting: presenter) { [weak self] result, error in
            guard let self else { return }
            if let error { return self.finish(provider: "google", error: error.localizedDescription) }
            guard let user = result?.user, let idToken = user.idToken?.tokenString else {
                return self.finish(provider: "google", error: "missing ID token")
            }
            self.finish(provider: "google", token: idToken, name: user.profile?.name)
        }
    }

    // Forwarded from Unity's openURL notification (CompanionNative.mm).
    @objc public func handle(url: URL) -> Bool {
        GIDSignIn.sharedInstance.handle(url)
    }

    // MARK: Helpers

    private func finish(provider: String, token: String? = nil, name: String? = nil, error: String? = nil) {
        var result: [String: Any] = ["ok": token != nil, "provider": provider]
        if let token { result["token"] = token }
        if let name { result["name"] = name }
        if let error { result["error"] = error }
        let json = (try? JSONSerialization.data(withJSONObject: result)).flatMap { String(data: $0, encoding: .utf8) } ?? "{\"ok\":false}"
        let done = completion
        completion = nil
        DispatchQueue.main.async { done?(json) }
    }

    private static var keyWindow: UIWindow? {
        UIApplication.shared.connectedScenes.compactMap { ($0 as? UIWindowScene)?.keyWindow }.first
    }

    private static func topViewController() -> UIViewController? {
        var top = keyWindow?.rootViewController
        while let presented = top?.presentedViewController { top = presented }
        return top
    }
}

extension CompanionAuth: ASAuthorizationControllerDelegate {
    public func authorizationController(controller: ASAuthorizationController, didCompleteWithAuthorization authorization: ASAuthorization) {
        guard let cred = authorization.credential as? ASAuthorizationAppleIDCredential,
              let tokenData = cred.identityToken, let token = String(data: tokenData, encoding: .utf8) else {
            return finish(provider: "apple", error: "missing identity token")
        }
        let name = [cred.fullName?.givenName, cred.fullName?.familyName].compactMap { $0 }.joined(separator: " ")
        finish(provider: "apple", token: token, name: name.isEmpty ? nil : name)
    }

    public func authorizationController(controller: ASAuthorizationController, didCompleteWithError error: Error) {
        finish(provider: "apple", error: error.localizedDescription)
    }
}

extension CompanionAuth: ASAuthorizationControllerPresentationContextProviding {
    public func presentationAnchor(for controller: ASAuthorizationController) -> ASPresentationAnchor {
        Self.keyWindow ?? ASPresentationAnchor()
    }
}
