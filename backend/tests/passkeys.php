<?php
// Test delle passkey (docs/06 §4): un autenticatore finto con firme vere (ES256 e RS256) crea la passkey
// e la usa per entrare; si provano anche gli attacchi (sfida riusata, origine sbagliata, firma alterata,
// contatore che non cresce, verifica dell'utente mancante). Incluso da run.php; usa $name dai test dell'account.

// chiave EC P-256 usa e getta, solo per i test (PHP 7.0 non sa generarle con openssl_pkey_new)
$ecPem = "-----BEGIN PRIVATE KEY-----\n"
    . "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgVlAyhmcyNuo1dl1Q\n"
    . "LfYij05c27tII0Pq3kPisRPSJgWhRANCAASxwn9euDBCE2KJnHF1DScqngnkak+z\n"
    . "SGRzAnZu0opg35qT9L3L+r5MVNQYjA3bK2xx9M7n+2N4xlnS/DFxSg0O\n"
    . "-----END PRIVATE KEY-----\n";

function cbor_head($major, $n)
{
    if ($n < 24) return chr(($major << 5) | $n);
    if ($n < 256) return chr(($major << 5) | 24) . chr($n);
    if ($n < 65536) return chr(($major << 5) | 25) . pack('n', $n);
    return chr(($major << 5) | 26) . pack('N', $n);
}
function cbor_int($n) { return $n >= 0 ? cbor_head(0, $n) : cbor_head(1, -1 - $n); }
function cbor_bytes($s) { return cbor_head(2, strlen($s)) . $s; }
function cbor_text($s) { return cbor_head(3, strlen($s)) . $s; }
function cbor_map(array $pairs)
{
    $out = cbor_head(5, count($pairs));
    foreach ($pairs as $p) $out .= $p[0] . $p[1];
    return $out;
}
function b64u($bin) { return rtrim(strtr(base64_encode($bin), '+/', '-_'), '='); }

/** Chiave pubblica COSE della chiave privata indicata. */
function cose_of($private)
{
    $details = openssl_pkey_get_details($private);
    if ($details['type'] === OPENSSL_KEYTYPE_EC) {
        // il punto non compresso sta in fondo alla SubjectPublicKeyInfo
        $der = base64_decode(preg_replace('/-----[^-]+-----|\s/', '', $details['key']));
        $point = substr($der, -64);
        return cbor_map(array(
            array(cbor_int(1), cbor_int(2)), array(cbor_int(3), cbor_int(-7)), array(cbor_int(-1), cbor_int(1)),
            array(cbor_int(-2), cbor_bytes(substr($point, 0, 32))), array(cbor_int(-3), cbor_bytes(substr($point, 32, 32))),
        ));
    }
    return cbor_map(array(
        array(cbor_int(1), cbor_int(3)), array(cbor_int(3), cbor_int(-257)),
        array(cbor_int(-1), cbor_bytes($details['rsa']['n'])), array(cbor_int(-2), cbor_bytes($details['rsa']['e'])),
    ));
}

/** Risposta di navigator.credentials.create() per le opzioni ricevute. */
function create_response($options, $private, $credId, $origin = 'https://samozzux.github.io', $flags = 0x45)
{
    $client = json_encode(array('type' => 'webauthn.create', 'challenge' => $options['publicKey']['challenge'], 'origin' => $origin, 'crossOrigin' => false));
    $rpId = $options['publicKey']['rp']['id'];
    $auth = hash('sha256', $rpId, true) . chr($flags) . pack('N', 0) . str_repeat("\0", 16) . pack('n', strlen($credId)) . $credId . cose_of($private);
    $att = cbor_map(array(array(cbor_text('fmt'), cbor_text('none')), array(cbor_text('attStmt'), cbor_map(array())), array(cbor_text('authData'), cbor_bytes($auth))));
    return array('challengeId' => $options['challengeId'], 'credentialId' => b64u($credId), 'clientDataJSON' => b64u($client), 'attestationObject' => b64u($att), 'name' => 'Safari sul Mac');
}

/** Risposta di navigator.credentials.get(): firma su authenticatorData || SHA-256(clientDataJSON). */
function get_response($options, $private, $credId, $count, $origin = 'https://samozzux.github.io', $flags = 0x05)
{
    $client = json_encode(array('type' => 'webauthn.get', 'challenge' => $options['publicKey']['challenge'], 'origin' => $origin));
    $auth = hash('sha256', $options['publicKey']['rpId'], true) . chr($flags) . pack('N', $count);
    openssl_sign($auth . hash('sha256', $client, true), $signature, $private, OPENSSL_ALGO_SHA256);
    return array(
        'challengeId' => $options['challengeId'], 'credentialId' => b64u($credId), 'clientDataJSON' => b64u($client),
        'authenticatorData' => b64u($auth), 'signature' => b64u($signature), 'userHandle' => '',
        'deviceId' => 'web-passkey-' . substr(md5($credId), 0, 8), 'deviceName' => 'Safari sul Mac', 'platform' => 'web',
    );
}

$login = api('POST', '/api/auth/login', array('username' => $name, 'password' => 'password-segreta', 'deviceId' => 'web-pk-mac-0001', 'deviceName' => 'Safari sul Mac', 'platform' => 'web'));
$access = $login['json']['accessToken'];
$ec = openssl_pkey_get_private($ecPem);
$ecId = random_bytes(16);

// --- creazione della passkey ---
$r = api('POST', '/api/auth/passkey/register/options', array());
check('passkey: per crearla serve l\'accesso', $r['status'] === 401, errorCode($r));
$r = api('POST', '/api/auth/passkey/register/options', array(), $access);
$opt = $r['json'];
check('passkey: opzioni di creazione', $r['status'] === 200 && $opt['publicKey']['rp']['id'] === 'samozzux.github.io' && strlen($opt['publicKey']['challenge']) === 43, $r['body']);
check('passkey: chiave residente e verifica dell\'utente', $opt['publicKey']['authenticatorSelection']['residentKey'] === 'required' && $opt['publicKey']['authenticatorSelection']['userVerification'] === 'required');
check('passkey: ES256 e RS256', $opt['publicKey']['pubKeyCredParams'][0]['alg'] === -7 && $opt['publicKey']['pubKeyCredParams'][1]['alg'] === -257);
check('passkey: identificativo utente opaco', strpos($opt['publicKey']['user']['id'], $name) === false && strlen($opt['publicKey']['user']['id']) >= 16);

$bad = create_response($opt, $ec, $ecId, 'https://esempio-malevolo.test');
$r = api('POST', '/api/auth/passkey/register/verify', $bad, $access);
check('passkey: origine di un altro sito rifiutata', $r['status'] === 400 && errorCode($r) === 'invalid_passkey', errorCode($r));
$r = api('POST', '/api/auth/passkey/register/verify', create_response($opt, $ec, $ecId), $access);
check('passkey: la sfida si usa una volta sola', $r['status'] === 400 && errorCode($r) === 'invalid_challenge', errorCode($r));

$opt = api('POST', '/api/auth/passkey/register/options', array(), $access)['json'];
$r = api('POST', '/api/auth/passkey/register/verify', create_response($opt, $ec, $ecId, 'https://samozzux.github.io', 0x41), $access);
check('passkey: senza verifica dell\'utente (UV) rifiutata', $r['status'] === 400 && errorCode($r) === 'invalid_passkey', errorCode($r));

$opt = api('POST', '/api/auth/passkey/register/options', array(), $access)['json'];
$r = api('POST', '/api/auth/passkey/register/verify', create_response($opt, $ec, $ecId), $access);
check('passkey ES256 creata (201)', $r['status'] === 201 && $r['json']['passkey']['name'] === 'Safari sul Mac', $r['body']);
$row = $testPdo->query("SELECT * FROM orb_passkeys WHERE credential_id = " . $testPdo->quote(b64u($ecId)))->fetch();
check('passkey: salvata solo la chiave pubblica', $row && strpos($row['public_key'], '-----BEGIN PUBLIC KEY-----') === 0 && (int)$row['alg'] === -7);

$opt = api('POST', '/api/auth/passkey/register/options', array(), $access)['json'];
check('passkey: quella già creata è esclusa', count($opt['publicKey']['excludeCredentials']) === 1 && $opt['publicKey']['excludeCredentials'][0]['id'] === b64u($ecId));
$r = api('POST', '/api/auth/passkey/register/verify', create_response($opt, $ec, $ecId), $access);
check('passkey: la stessa non si registra due volte', $r['status'] === 409 && errorCode($r) === 'passkey_exists', errorCode($r));

// --- accesso con la passkey ---
$r = api('POST', '/api/auth/passkey/login/options', array());
$lo = $r['json'];
check('passkey: opzioni di accesso senza nome utente', $r['status'] === 200 && $lo['publicKey']['rpId'] === 'samozzux.github.io' && $lo['publicKey']['allowCredentials'] === array(), $r['body']);
$r = api('POST', '/api/auth/passkey/login/verify', get_response($lo, $ec, $ecId, 1));
check('passkey: si entra senza password', $r['status'] === 200 && isset($r['json']['accessToken'], $r['json']['refreshToken']) && $r['json']['user']['username'] === $name, $r['body']);
$me = api('GET', '/api/me', null, $r['json']['accessToken']);
check('passkey: il token funziona', $me['status'] === 200 && $me['json']['user']['username'] === $name, errorCode($me));
$r2 = api('POST', '/api/auth/refresh', array('refreshToken' => $r['json']['refreshToken']));
check('passkey: anche il rinnovo funziona', $r2['status'] === 200, errorCode($r2));

$lo = api('POST', '/api/auth/passkey/login/options', array())['json'];
$resp = get_response($lo, $ec, $ecId, 2);
$sig = WebAuthnTestFlip($resp['signature']);
$resp['signature'] = $sig;
$r = api('POST', '/api/auth/passkey/login/verify', $resp);
check('passkey: firma alterata rifiutata', $r['status'] === 401 && errorCode($r) === 'invalid_passkey', errorCode($r));

$lo = api('POST', '/api/auth/passkey/login/options', array())['json'];
$r = api('POST', '/api/auth/passkey/login/verify', get_response($lo, $ec, $ecId, 1));
check('passkey: contatore che non cresce (copia) rifiutato', $r['status'] === 401 && errorCode($r) === 'passkey_cloned', errorCode($r));

$lo = api('POST', '/api/auth/passkey/login/options', array())['json'];
$r = api('POST', '/api/auth/passkey/login/verify', get_response($lo, $ec, random_bytes(16), 3));
check('passkey sconosciuta rifiutata', $r['status'] === 401 && errorCode($r) === 'unknown_passkey', errorCode($r));

$lo = api('POST', '/api/auth/passkey/login/options', array())['json'];
$r = api('POST', '/api/auth/passkey/login/verify', get_response($lo, $ec, $ecId, 4, 'https://samozzux.github.io', 0x01));
check('passkey: accesso senza verifica dell\'utente rifiutato', $r['status'] === 400 && errorCode($r) === 'invalid_passkey', errorCode($r));

// --- RS256 (es. Windows Hello) e sviluppo in locale ---
$rsa = openssl_pkey_new(array('private_key_bits' => 2048, 'private_key_type' => OPENSSL_KEYTYPE_RSA));
$rsaId = random_bytes(32);
$local = request('POST', '/api/auth/passkey/register/options', array('Origin: http://localhost:8080', 'Content-Type: application/json', 'Authorization: Bearer ' . $access), '{}');
check('passkey: in locale l\'RP ID è localhost', $local['status'] === 200 && $local['json']['publicKey']['rp']['id'] === 'localhost', $local['body']);
$r = api('POST', '/api/auth/passkey/register/verify', create_response($local['json'], $rsa, $rsaId, 'http://localhost:8080'), $access);
check('passkey RS256 creata', $r['status'] === 201, $r['body']);
$lo = request('POST', '/api/auth/passkey/login/options', array('Origin: http://localhost:8080', 'Content-Type: application/json'), '{}');
$r = api('POST', '/api/auth/passkey/login/verify', get_response($lo['json'], $rsa, $rsaId, 0, 'http://localhost:8080'));
check('passkey RS256: si entra', $r['status'] === 200 && $r['json']['user']['username'] === $name, $r['body']);

// --- elenco ed eliminazione ---
$r = api('GET', '/api/auth/passkeys', null, $access);
check('passkey: elenco', $r['status'] === 200 && count($r['json']['passkeys']) === 2, $r['body']);
$pid = $r['json']['passkeys'][1]['id'];
$r = api('DELETE', '/api/auth/passkeys/' . $pid, null, $accessOther);
check('passkey: non si eliminano quelle degli altri', $r['status'] === 404, errorCode($r));
$r = api('DELETE', '/api/auth/passkeys/' . $pid, null, $access);
check('passkey eliminata (204)', $r['status'] === 204, 'status ' . $r['status']);
$lo = request('POST', '/api/auth/passkey/login/options', array('Origin: http://localhost:8080', 'Content-Type: application/json'), '{}');
$r = api('POST', '/api/auth/passkey/login/verify', get_response($lo['json'], $rsa, $rsaId, 0, 'http://localhost:8080'));
check('passkey eliminata: non si entra più', $r['status'] === 401 && errorCode($r) === 'unknown_passkey', errorCode($r));

function WebAuthnTestFlip($b64)
{
    $raw = base64_decode(strtr($b64, '-_', '+/') . str_repeat('=', (4 - strlen($b64) % 4) % 4));
    $raw[strlen($raw) - 3] = chr(ord($raw[strlen($raw) - 3]) ^ 0x01);
    return b64u($raw);
}
