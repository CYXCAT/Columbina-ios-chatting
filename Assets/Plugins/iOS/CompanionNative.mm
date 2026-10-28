// C entry points for Companion's DllImport("__Internal") declarations, forwarding to the Swift
// implementations (CompanionSpeech.swift, CompanionAuth.swift) and back to Unity through
// UnitySendMessage on the "CompanionNative" GameObject (Companion.NativeBridge).

#import <Foundation/Foundation.h>
#import <Security/Security.h>
#import <UnityFramework/UnityFramework-Swift.h>
#include "UnityInterface.h"
#include "AppDelegateListener.h"

static const char* kReceiver = "CompanionNative";

static void Send(const char* method, NSString* message)
{
    UnitySendMessage(kReceiver, method, (message ?: @"").UTF8String);
}

static NSString* Str(const char* s) { return s ? [NSString stringWithUTF8String:s] : @""; }

// GoogleSignIn must see the OAuth redirect URL; UnityAppController posts it as kUnityOnOpenURL.
__attribute__((constructor)) static void CompanionObserveOpenURL(void)
{
    [[NSNotificationCenter defaultCenter] addObserverForName:kUnityOnOpenURL object:nil queue:NSOperationQueue.mainQueue
        usingBlock:^(NSNotification* note) {
            NSURL* url = note.userInfo[@"url"];
            if (url) [CompanionAuth.shared handleWithUrl:url];
        }];
}

extern "C" {

// ---- speech
void _CompanionSpeechStart(const char* locale)
{
    [CompanionSpeech.shared startWithLocale:Str(locale)
        onPartial:^(NSString* t) { Send("OnSpeechPartial", t); }
        onFinal:^(NSString* t) { Send("OnSpeechFinal", t); }
        onError:^(NSString* m) { Send("OnSpeechError", m); }];
}

void _CompanionSpeechStop(void)
{
    [CompanionSpeech.shared stop];
}

// ---- auth
void _CompanionAppleSignIn(const char* hashedNonce)
{
    [CompanionAuth.shared signInWithAppleWithHashedNonce:Str(hashedNonce)
        completion:^(NSString* json) { Send("OnAuthResult", json); }];
}

void _CompanionGoogleSignIn(void)
{
    [CompanionAuth.shared signInWithGoogleWithCompletion:^(NSString* json) { Send("OnAuthResult", json); }];
}

// ---- keychain (generic password items scoped to this app)
static NSMutableDictionary* KeychainQuery(const char* key)
{
    return [@{
        (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: @"companion",
        (__bridge id)kSecAttrAccount: Str(key),
    } mutableCopy];
}

void _CompanionKeychainSet(const char* key, const char* value)
{
    NSMutableDictionary* q = KeychainQuery(key);
    SecItemDelete((__bridge CFDictionaryRef)q);
    q[(__bridge id)kSecValueData] = [Str(value) dataUsingEncoding:NSUTF8StringEncoding];
    q[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly;
    SecItemAdd((__bridge CFDictionaryRef)q, NULL);
}

// Returns a malloc'd UTF-8 string (freed by the C# marshaller via Marshal.FreeHGlobal) or NULL.
char* _CompanionKeychainGet(const char* key)
{
    NSMutableDictionary* q = KeychainQuery(key);
    q[(__bridge id)kSecReturnData] = @YES;
    q[(__bridge id)kSecMatchLimit] = (__bridge id)kSecMatchLimitOne;
    CFTypeRef data = NULL;
    if (SecItemCopyMatching((__bridge CFDictionaryRef)q, &data) != errSecSuccess || data == NULL) return NULL;
    NSData* d = (__bridge_transfer NSData*)data;
    char* out = (char*)malloc(d.length + 1);
    memcpy(out, d.bytes, d.length);
    out[d.length] = 0;
    return out;
}

void _CompanionKeychainDelete(const char* key)
{
    SecItemDelete((__bridge CFDictionaryRef)KeychainQuery(key));
}

}
