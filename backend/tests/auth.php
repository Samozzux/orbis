<?php
// Test dell'account (docs/06 §4 e §7): incluso da run.php quando c'è il database.
// Usa: api(), check(), errorCode(), $testPdo.

$suffix = substr(md5((string)mt_rand()), 0, 6);
$name = 'samu_' . $suffix;
$deviceA = array('deviceId' => 'iphone-' . $suffix . '-aaaa', 'deviceName' => 'iPhone di prova', 'platform' => 'ios');
$deviceB = array('deviceId' => 'web-' . $suffix . '-bbbb', 'deviceName' => 'Safari sul Mac', 'platform' => 'web');

// --- registrazione ---
$r = api('POST', '/api/auth/register', array_merge(array('username' => 'ab', 'password' => 'password123'), $deviceA));
check('registrazione: nome troppo corto', $r['status'] === 400 && errorCode($r) === 'invalid_username', errorCode($r));
$r = api('POST', '/api/auth/register', array_merge(array('username' => 'nome con spazi', 'password' => 'password123'), $deviceA));
check('registrazione: caratteri non ammessi', $r['status'] === 400 && errorCode($r) === 'invalid_username', errorCode($r));
$r = api('POST', '/api/auth/register', array_merge(array('username' => $name, 'password' => 'corta'), $deviceA));
check('registrazione: password corta', $r['status'] === 400 && errorCode($r) === 'invalid_password', errorCode($r));
$r = api('POST', '/api/auth/register', array('username' => $name, 'password' => 'password123'));
check('registrazione: serve il dispositivo', $r['status'] === 400 && errorCode($r) === 'invalid_device', errorCode($r));
$r = request('POST', '/api/auth/register', array('Content-Type: application/json'), '{non è json');
check('JSON non valido → 400', $r['status'] === 400 && errorCode($r) === 'invalid_json', errorCode($r));

$r = api('POST', '/api/auth/register', array_merge(array('username' => $name, 'password' => 'password-segreta', 'displayName' => 'Samuele'), $deviceA));
check('registrazione riuscita (201)', $r['status'] === 201, errorCode($r));
check('registrazione: token di accesso e di rinnovo', isset($r['json']['accessToken'], $r['json']['refreshToken']) && strlen($r['json']['refreshToken']) === 43);
check('registrazione: accesso da 1 ora', isset($r['json']['accessExpiresIn']) && $r['json']['accessExpiresIn'] === 3600);
check('registrazione: profilo', isset($r['json']['user']) && $r['json']['user']['username'] === $name && $r['json']['user']['displayName'] === 'Samuele');
$accessA = $r['json']['accessToken'];
$refreshA = $r['json']['refreshToken'];
$userId = $r['json']['user']['id'];

$r = api('POST', '/api/auth/register', array_merge(array('username' => strtoupper($name), 'password' => 'password123'), $deviceB));
check('registrazione: nome già usato (maiuscole comprese)', $r['status'] === 409 && errorCode($r) === 'username_taken', errorCode($r));

$row = $testPdo->query('SELECT password_hash FROM orb_users WHERE id = ' . (int)$userId)->fetch();
check('password salvata solo come hash bcrypt', $row && strpos($row['password_hash'], '$2y$') === 0 && strpos($row['password_hash'], 'password-segreta') === false);
$hashes = $testPdo->query('SELECT token_hash FROM orb_refresh_tokens WHERE user_id = ' . (int)$userId)->fetchAll(PDO::FETCH_COLUMN);
check('refresh token salvato solo come hash', count($hashes) === 1 && $hashes[0] === hash('sha256', $refreshA) && $hashes[0] !== $refreshA);

// --- profilo ---
$r = api('GET', '/api/me');
check('profilo senza accesso → 401', $r['status'] === 401 && errorCode($r) === 'unauthorized', errorCode($r));
$r = api('GET', '/api/me', null, $accessA);
check('profilo con il token', $r['status'] === 200 && $r['json']['user']['id'] === $userId, errorCode($r));
$p = explode('.', $accessA);
$tampered = $p[0] . '.' . \Orbis\Api\Auth\Jwt::b64(json_encode(array('sub' => '999', 'exp' => time() + 600))) . '.' . $p[2];
$r = api('GET', '/api/me', null, $tampered);
check('profilo con token alterato → 401', $r['status'] === 401 && errorCode($r) === 'invalid_token', errorCode($r));
$r = api('PATCH', '/api/me', array('displayName' => "  Samuele C.\n "), $accessA);
check('cambio del nome visualizzato', $r['status'] === 200 && $r['json']['user']['displayName'] === 'Samuele C.', errorCode($r));
$r = api('PATCH', '/api/me', array('displayName' => '   '), $accessA);
check('nome visualizzato vuoto rifiutato', $r['status'] === 400, errorCode($r));

// --- accesso ---
$r = api('POST', '/api/auth/login', array_merge(array('username' => $name, 'password' => 'sbagliata'), $deviceB));
check('accesso: password sbagliata → 401', $r['status'] === 401 && errorCode($r) === 'invalid_credentials', errorCode($r));
$r = api('POST', '/api/auth/login', array_merge(array('username' => 'nessuno_' . $suffix, 'password' => 'password123'), $deviceB));
check('accesso: utente inesistente → stesso errore', $r['status'] === 401 && errorCode($r) === 'invalid_credentials', errorCode($r));
$r = api('POST', '/api/auth/login', array_merge(array('username' => $name, 'password' => 'password-segreta'), $deviceB));
check('accesso da un secondo dispositivo', $r['status'] === 200 && isset($r['json']['accessToken']), errorCode($r));
$accessB = $r['json']['accessToken'];
$refreshB = $r['json']['refreshToken'];

// --- sessioni ---
$r = api('GET', '/api/auth/sessions', null, $accessA);
$sessions = isset($r['json']['sessions']) ? $r['json']['sessions'] : array();
$current = array_values(array_filter($sessions, function ($s) { return $s['current']; }));
check('due sessioni aperte', count($sessions) === 2, json_encode($sessions));
check('la sessione corrente è quella dell\'iPhone', count($current) === 1 && $current[0]['platform'] === 'ios' && $current[0]['deviceName'] === 'iPhone di prova');

// --- rinnovo con rotazione ---
$r = api('POST', '/api/auth/refresh', array('refreshToken' => $refreshA));
check('rinnovo riuscito', $r['status'] === 200 && isset($r['json']['refreshToken']), errorCode($r));
$refreshA2 = $r['json']['refreshToken'];
check('rinnovo: refresh token nuovo', $refreshA2 !== $refreshA);
$r2 = api('GET', '/api/me', null, $r['json']['accessToken']);
check('rinnovo: il nuovo token di accesso funziona', $r2['status'] === 200, errorCode($r2));

$r = api('POST', '/api/auth/refresh', array('refreshToken' => $refreshA));
check('riuso del vecchio refresh token → 401', $r['status'] === 401 && errorCode($r) === 'refresh_reused', errorCode($r));
$r = api('POST', '/api/auth/refresh', array('refreshToken' => $refreshA2));
check('dopo il riuso si chiude tutta la sessione del dispositivo', $r['status'] === 401, errorCode($r));
$r = api('GET', '/api/auth/sessions', null, $accessB);
check('resta solo la sessione del Mac', isset($r['json']['sessions']) && count($r['json']['sessions']) === 1 && $r['json']['sessions'][0]['platform'] === 'web', $r['body']);
$r = api('POST', '/api/auth/refresh', array('refreshToken' => 'non-valido'));
check('refresh token non valido → 401', $r['status'] === 401 && errorCode($r) === 'invalid_refresh', errorCode($r));

// --- uscita e chiusura di una sessione ---
$r = api('POST', '/api/auth/login', array_merge(array('username' => $name, 'password' => 'password-segreta'), $deviceA));
$accessA3 = $r['json']['accessToken'];
$r = api('POST', '/api/auth/logout', array('refreshToken' => $refreshB));
check('uscita (204)', $r['status'] === 204, 'status ' . $r['status']);
$r = api('POST', '/api/auth/refresh', array('refreshToken' => $refreshB));
check('dopo l\'uscita il refresh token non vale più', $r['status'] === 401, errorCode($r));

$other = 'altro_' . $suffix;
$r = api('POST', '/api/auth/register', array_merge(array('username' => $other, 'password' => 'password123'), $deviceB));
$accessOther = $r['json']['accessToken'];
$r = api('GET', '/api/auth/sessions', null, $accessA3);
$sid = $r['json']['sessions'][0]['id'];
$r = api('DELETE', '/api/auth/sessions/' . $sid, null, $accessOther);
check('non si chiudono le sessioni degli altri (404)', $r['status'] === 404, errorCode($r));
$r = api('DELETE', '/api/auth/sessions/' . $sid, null, $accessA3);
check('chiusura di una propria sessione (204)', $r['status'] === 204, 'status ' . $r['status']);
$r = api('GET', '/api/auth/sessions', null, $accessA3);
check('sessione chiusa non più in elenco', isset($r['json']['sessions']) && count($r['json']['sessions']) === 0, $r['body']);

// --- CORS per le chiamate con corpo JSON ---
$r = request('OPTIONS', '/api/auth/login', array('Origin: https://samozzux.github.io', 'Access-Control-Request-Method: POST', 'Access-Control-Request-Headers: content-type'));
check('CORS: preflight del login', $r['status'] === 204 && isset($r['headers']['access-control-allow-origin']));

// --- limite ai tentativi ---
$victim = 'limite_' . $suffix;
api('POST', '/api/auth/register', array_merge(array('username' => $victim, 'password' => 'password123'), $deviceB));
for ($i = 0; $i < 10; $i++) {
    api('POST', '/api/auth/login', array_merge(array('username' => $victim, 'password' => 'sbagliata' . $i), $deviceB));
}
$r = api('POST', '/api/auth/login', array_merge(array('username' => $victim, 'password' => 'password123'), $deviceB));
check('dopo 10 errori in 15 minuti → 429', $r['status'] === 429 && errorCode($r) === 'too_many_attempts', errorCode($r));
check('429 con Retry-After', isset($r['headers']['retry-after']) && (int)$r['headers']['retry-after'] > 0);
$r = api('POST', '/api/auth/login', array_merge(array('username' => $name, 'password' => 'password-segreta'), $deviceB));
check('gli altri utenti dallo stesso IP entrano ancora', $r['status'] === 200, errorCode($r));
$ips = $testPdo->query('SELECT DISTINCT ip_hash FROM orb_login_attempts')->fetchAll(PDO::FETCH_COLUMN);
check('IP salvato solo come HMAC', count($ips) === 1 && strlen($ips[0]) === 64 && $ips[0] !== '127.0.0.1');
