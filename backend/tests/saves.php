<?php
// Test dei salvataggi nel cloud (docs/06 §5 e §7): incluso da run.php quando c'è il database.
// Usa: api(), request(), check(), errorCode(), $storageDir.

function uuid4()
{
    $b = random_bytes(16);
    $b[6] = chr((ord($b[6]) & 0x0f) | 0x40);
    $b[8] = chr((ord($b[8]) & 0x3f) | 0x80);
    $h = bin2hex($b);
    return substr($h, 0, 8) . '-' . substr($h, 8, 4) . '-' . substr($h, 12, 4) . '-' . substr($h, 16, 4) . '-' . substr($h, 20, 12);
}

/** Carica un salvataggio intero; restituisce l'ultima risposta (finish, o quella che ha fallito). */
function uploadSave($token, $uuid, $data, $base, $device, $name = 'Chieti 2026')
{
    $r = api('POST', '/api/saves/' . $uuid . '/upload/start', array_merge(array(
        'baseRevision' => $base, 'size' => strlen($data), 'checksum' => hash('sha256', $data),
        'name' => $name, 'cityName' => 'Chieti', 'meta' => array('level' => 3, 'cash' => 50000, 'gameDate' => '2026-12-01'),
    ), $device), $token);
    if ($r['status'] !== 201) {
        return $r;
    }
    $id = $r['json']['uploadId'];
    $chunk = $r['json']['chunkSize'];
    for ($i = 0; $i < $r['json']['chunkCount']; $i++) {
        $c = chunkPut($token, $uuid, $id, $i, substr($data, $i * $chunk, $chunk));
        if ($c['status'] !== 204) {
            return $c;
        }
    }
    return api('POST', '/api/saves/' . $uuid . '/upload/' . $id . '/finish', array(), $token);
}

function chunkPut($token, $uuid, $uploadId, $n, $bytes)
{
    return request('PUT', '/api/saves/' . $uuid . '/upload/' . $uploadId . '/chunk/' . $n,
        array('Authorization: Bearer ' . $token, 'Content-Type: application/octet-stream'), $bytes);
}

$sfx = substr(md5((string)mt_rand()), 0, 6);
$devIphone = array('deviceId' => 'iphone-' . $sfx . '-save', 'deviceName' => 'iPhone di Samuele', 'platform' => 'ios');
$devMac = array('deviceId' => 'mac-' . $sfx . '-save', 'deviceName' => 'Safari sul Mac', 'platform' => 'web');
$r = api('POST', '/api/auth/register', array_merge(array('username' => 'cloud_' . $sfx, 'password' => 'password123'), $devIphone));
$tok = $r['json']['accessToken'];
$uid = $r['json']['user']['id'];
$r = api('POST', '/api/auth/login', array_merge(array('username' => 'cloud_' . $sfx, 'password' => 'password123'), $devMac));
$tokMac = $r['json']['accessToken'];

$uuid = uuid4();
$r = api('GET', '/api/saves');
check('salvataggi: serve l\'accesso', $r['status'] === 401, errorCode($r));
$r = api('GET', '/api/saves', null, $tok);
check('salvataggi: elenco vuoto', $r['status'] === 200 && $r['json']['saves'] === array(), $r['body']);
$r = api('GET', '/api/saves/non-un-uuid', null, $tok);
check('salvataggi: identificativo non valido → 400', $r['status'] === 400 && errorCode($r) === 'invalid_uuid', errorCode($r));
$r = api('POST', '/api/saves/' . $uuid . '/upload/start', array_merge(array('size' => 10, 'checksum' => str_repeat('a', 64)), $devIphone), $tok);
check('upload: serve la revisione di partenza', $r['status'] === 400 && errorCode($r) === 'invalid_revision', errorCode($r));
$r = api('POST', '/api/saves/' . $uuid . '/upload/start', array_merge(array('baseRevision' => 0, 'size' => 70000000, 'checksum' => str_repeat('a', 64)), $devIphone), $tok);
check('upload: troppo grande → 413', $r['status'] === 413, errorCode($r));

// salvataggio di 1,2 MB: tre pezzi da 512 KB
$data = random_bytes(1200000);
$r = api('POST', '/api/saves/' . $uuid . '/upload/start', array_merge(array(
    'baseRevision' => 0, 'size' => strlen($data), 'checksum' => hash('sha256', $data), 'name' => 'Chieti 2026', 'cityName' => 'Chieti',
    'meta' => array('level' => 3, 'gameDate' => '2026-12-01'),
), $devIphone), $tok);
check('upload: inizio (201)', $r['status'] === 201 && $r['json']['chunkCount'] === 3 && $r['json']['chunkSize'] === 524288, $r['body']);
$up = $r['json']['uploadId'];
$r = api('POST', '/api/saves/' . $uuid . '/upload/' . $up . '/finish', array(), $tok);
check('upload: chiusura con pezzi mancanti → 409', $r['status'] === 409 && errorCode($r) === 'upload_incomplete', errorCode($r));
$r = chunkPut($tok, $uuid, $up, 0, 'troppo corto');
check('upload: pezzo della misura sbagliata → 400', $r['status'] === 400 && errorCode($r) === 'invalid_chunk', errorCode($r));
$r = chunkPut($tok, $uuid, $up, 3, 'x');
check('upload: pezzo oltre la fine → 400', $r['status'] === 400, errorCode($r));
$intruder = api('POST', '/api/auth/register', array_merge(array('username' => 'intruso_' . $sfx, 'password' => 'password123'), $devMac));
$r = chunkPut($intruder['json']['accessToken'], $uuid, $up, 0, substr($data, 0, 524288));
check('upload: un altro utente non può aggiungere pezzi (404)', $r['status'] === 404 && errorCode($r) === 'upload_not_found', errorCode($r));
$ok = true;
foreach (array(0, 1, 1, 2) as $i) { // il pezzo 1 rimandato due volte (rete caduta)
    $c = chunkPut($tok, $uuid, $up, $i, substr($data, $i * 524288, 524288));
    $ok = $ok && $c['status'] === 204;
}
check('upload: pezzi accettati (anche rimandati)', $ok);
$r = api('POST', '/api/saves/' . $uuid . '/upload/' . $up . '/finish', array(), $tok);
check('upload: chiusura → revisione 1', $r['status'] === 200 && $r['json']['save']['revision'] === 1, $r['body']);
check('upload: metadati', isset($r['json']['save']) && $r['json']['save']['sizeBytes'] === strlen($data) && $r['json']['save']['deviceName'] === 'iPhone di Samuele'
    && $r['json']['save']['meta']['level'] === 3 && $r['json']['save']['cityName'] === 'Chieti');
$r = api('POST', '/api/saves/' . $uuid . '/upload/' . $up . '/finish', array(), $tok);
check('upload: chiuso una volta sola', $r['status'] === 404, errorCode($r));

$r = api('GET', '/api/saves', null, $tokMac);
check('elenco dal Mac: la partita dell\'iPhone', count($r['json']['saves']) === 1 && $r['json']['saves'][0]['uuid'] === $uuid && $r['json']['saves'][0]['revision'] === 1, $r['body']);

// --- download ---
$r = request('GET', '/api/saves/' . $uuid . '/download', array('Authorization: Bearer ' . $tokMac));
check('download completo identico', $r['status'] === 200 && $r['body'] === $data, 'status ' . $r['status'] . ', ' . strlen((string)$r['body']) . ' byte');
check('download: revisione e checksum nelle intestazioni', isset($r['headers']['x-orbis-revision'], $r['headers']['etag'])
    && $r['headers']['x-orbis-revision'] === '1' && trim($r['headers']['etag'], '"') === hash('sha256', $data));
$r = request('GET', '/api/saves/' . $uuid . '/download', array('Authorization: Bearer ' . $tokMac, 'Range: bytes=100-199'));
check('download a pezzi (206)', $r['status'] === 206 && $r['body'] === substr($data, 100, 100) && $r['headers']['content-range'] === 'bytes 100-199/' . strlen($data));
$r = request('GET', '/api/saves/' . $uuid . '/download', array('Authorization: Bearer ' . $tokMac, 'Range: bytes=99999999-'));
check('download: intervallo fuori dal file → 416', $r['status'] === 416, 'status ' . $r['status']);

// --- conflitti ---
$small = 'partita continuata sul Mac ' . str_repeat('x', 5000);
$r = uploadSave($tokMac, $uuid, $small, 0, $devMac);
check('conflitto: il Mac parte dalla revisione 0 ma nel cloud c\'è la 1', $r['status'] === 409 && errorCode($r) === 'conflict' && $r['json']['cloud']['revision'] === 1, $r['body']);
$r = uploadSave($tokMac, $uuid, $small, 1, $devMac);
check('il Mac continua dalla revisione 1 → 2', $r['status'] === 200 && $r['json']['save']['revision'] === 2 && $r['json']['save']['deviceName'] === 'Safari sul Mac', $r['body']);
$r = uploadSave($tok, $uuid, 'vecchia versione iPhone', 1, $devIphone);
check('conflitto: l\'iPhone è rimasto alla 1, il cloud è alla 2 (dal Mac)', $r['status'] === 409 && $r['json']['cloud']['revision'] === 2
    && $r['json']['cloud']['deviceName'] === 'Safari sul Mac', $r['body']);
$files = glob($storageDir . '/saves/' . $uid . '/*.sav');
check('nel server resta solo l\'ultima revisione', count($files) === 1 && substr($files[0], -6) === '.2.sav', json_encode($files));

// due upload partiti dalla stessa revisione: vince il primo che chiude
$a = 'versione A';
$b = 'versione B';
$ra = api('POST', '/api/saves/' . $uuid . '/upload/start', array_merge(array('baseRevision' => 2, 'size' => strlen($a), 'checksum' => hash('sha256', $a)), $devIphone), $tok);
$rb = api('POST', '/api/saves/' . $uuid . '/upload/start', array_merge(array('baseRevision' => 2, 'size' => strlen($b), 'checksum' => hash('sha256', $b)), $devMac), $tokMac);
chunkPut($tok, $uuid, $ra['json']['uploadId'], 0, $a);
chunkPut($tokMac, $uuid, $rb['json']['uploadId'], 0, $b);
$fa = api('POST', '/api/saves/' . $uuid . '/upload/' . $ra['json']['uploadId'] . '/finish', array(), $tok);
$fb = api('POST', '/api/saves/' . $uuid . '/upload/' . $rb['json']['uploadId'] . '/finish', array(), $tokMac);
check('upload contemporanei: il primo diventa la revisione 3', $fa['status'] === 200 && $fa['json']['save']['revision'] === 3, $fa['body']);
check('upload contemporanei: il secondo trova il conflitto', $fb['status'] === 409 && errorCode($fb) === 'conflict' && $fb['json']['cloud']['revision'] === 3, $fb['body']);

$bad = 'contenuto';
$r = api('POST', '/api/saves/' . $uuid . '/upload/start', array_merge(array('baseRevision' => 3, 'size' => strlen($bad), 'checksum' => hash('sha256', 'altro')), $devIphone), $tok);
chunkPut($tok, $uuid, $r['json']['uploadId'], 0, $bad);
$r = api('POST', '/api/saves/' . $uuid . '/upload/' . $r['json']['uploadId'] . '/finish', array(), $tok);
check('checksum sbagliato → 422', $r['status'] === 422 && errorCode($r) === 'checksum_mismatch', errorCode($r));
$r = request('GET', '/api/saves/' . $uuid . '/download', array('Authorization: Bearer ' . $tok));
check('dopo il 422 il cloud ha ancora la revisione 3', $r['body'] === $a && $r['headers']['x-orbis-revision'] === '3');

// --- altri utenti ed eliminazione ---
// (un sesto account dallo stesso IP nella stessa ora verrebbe rifiutato: si riusa quello dell'intruso)
$tokOther = $intruder['json']['accessToken'];
$r = request('GET', '/api/saves/' . $uuid . '/download', array('Authorization: Bearer ' . $tokOther));
check('un altro utente non scarica la partita (404)', $r['status'] === 404);
$r = api('DELETE', '/api/saves/' . $uuid, null, $tokOther);
check('un altro utente non la elimina (404)', $r['status'] === 404, errorCode($r));
$r = api('DELETE', '/api/saves/' . $uuid, null, $tok);
check('eliminazione (204)', $r['status'] === 204, 'status ' . $r['status']);
$r = api('GET', '/api/saves/' . $uuid, null, $tok);
check('dopo l\'eliminazione → 404', $r['status'] === 404, errorCode($r));
check('file eliminato dal server', count(glob($storageDir . '/saves/' . $uid . '/*.sav')) === 0);
$r = api('POST', '/api/auth/register', array_merge(array('username' => 'sesto_' . $sfx, 'password' => 'password123'), $devMac));
check('sesto account nella stessa ora dallo stesso IP → 429', $r['status'] === 429 && errorCode($r) === 'too_many_attempts', errorCode($r));
check('nessun upload rimasto a metà', count(glob($storageDir . '/uploads/*/*')) === 0, json_encode(glob($storageDir . '/uploads/*/*')));
