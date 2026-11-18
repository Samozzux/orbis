// Keychain e Face ID per Orbis (docs/06 §4, docs/01 §10).
// Funzioni C (@_cdecl) chiamate da Assets/Orbis/Platform/Ios/IosSecurity.cs.
// Elementi normali: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly.
// Elementi protetti: SecAccessControl .biometryCurrentSet (leggerli mostra Face ID; se cambiano i volti
// registrati l'elemento non si legge più e serve di nuovo la password).

import Foundation
import LocalAuthentication
import Security

private let orbisService = "it.orbis.secure"

private func orbisAccount(_ key: String, _ biometric: Int32) -> String {
    return biometric != 0 ? key + ".bio" : key
}

private func orbisDelete(_ account: String) {
    let query: [String: Any] = [
        kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: orbisService,
        kSecAttrAccount as String: account,
    ]
    SecItemDelete(query as CFDictionary)
}

@_cdecl("OrbisSecurity_BiometryAvailable")
public func OrbisSecurity_BiometryAvailable() -> Int32 {
    let context = LAContext()
    var error: NSError?
    return context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) ? 1 : 0
}

@_cdecl("OrbisSecurity_Set")
public func OrbisSecurity_Set(_ key: UnsafePointer<CChar>, _ value: UnsafePointer<CChar>, _ biometric: Int32) -> Int32 {
    let account = orbisAccount(String(cString: key), biometric)
    guard let data = String(cString: value).data(using: .utf8) else { return 0 }
    orbisDelete(account)
    var query: [String: Any] = [
        kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: orbisService,
        kSecAttrAccount as String: account,
        kSecValueData as String: data,
    ]
    if biometric != 0 {
        var error: Unmanaged<CFError>?
        guard let access = SecAccessControlCreateWithFlags(nil, kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly, .biometryCurrentSet, &error) else {
            return 0
        }
        query[kSecAttrAccessControl as String] = access
    } else {
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
    }
    return SecItemAdd(query as CFDictionary, nil) == errSecSuccess ? 1 : 0
}

public typealias OrbisSecurityCallback = @convention(c) (Int32, UnsafePointer<CChar>?) -> Void

@_cdecl("OrbisSecurity_Get")
public func OrbisSecurity_Get(_ key: UnsafePointer<CChar>, _ reason: UnsafePointer<CChar>, _ biometric: Int32, _ requestId: Int32,
                              _ callback: OrbisSecurityCallback) {
    let account = orbisAccount(String(cString: key), biometric)
    let prompt = String(cString: reason)
    // la lettura di un elemento protetto mostra Face ID e blocca: fuori dal thread principale
    DispatchQueue.global(qos: .userInitiated).async {
        var query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: orbisService,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        if biometric != 0 {
            let context = LAContext()
            context.localizedReason = prompt
            query[kSecUseAuthenticationContext as String] = context
        }
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        let text: String? = (status == errSecSuccess) ? (result as? Data).flatMap { String(data: $0, encoding: .utf8) } : nil
        DispatchQueue.main.async {
            if let text = text {
                text.withCString { callback(requestId, $0) }
            } else {
                callback(requestId, nil)
            }
        }
    }
}

@_cdecl("OrbisSecurity_Delete")
public func OrbisSecurity_Delete(_ key: UnsafePointer<CChar>, _ biometric: Int32) {
    orbisDelete(orbisAccount(String(cString: key), biometric))
}
