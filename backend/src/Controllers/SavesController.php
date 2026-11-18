<?php
namespace Orbis\Api\Controllers;

use Orbis\Api\Auth\Auth;
use Orbis\Api\Http\HttpError;
use Orbis\Api\Http\Request;
use Orbis\Api\Http\Response;
use Orbis\Api\Storage;
use PDO;

/**
 * Salvataggi nel cloud (docs/06 §5): elenco, upload a pezzi con revisione di partenza (conflitto 409 se nel
 * cloud c'è una revisione più nuova), download anche a pezzi (Range) ed eliminazione.
 */
class SavesController
{
    const MAX_SAVE_BYTES = 67108864; // 64 MB
    const MAX_SAVES = 30;
    const MAX_META_BYTES = 4096;
    const UUID = '/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/';

    public static function index(Request $request, array $params)
    {
        $user = Auth::user($request);
        $st = Auth::pdo()->prepare('SELECT * FROM orb_saves WHERE user_id = ? AND is_shared_snapshot = 0 ORDER BY updated_at DESC, id DESC');
        $st->execute(array($user['id']));
        $out = array();
        foreach ($st->fetchAll() as $row) {
            $out[] = self::meta($row);
        }
        Response::json(200, array('saves' => $out));
    }

    public static function show(Request $request, array $params)
    {
        $user = Auth::user($request);
        Response::json(200, array('save' => self::meta(self::find(Auth::pdo(), (int)$user['id'], self::uuid($params), true))));
    }

    public static function start(Request $request, array $params)
    {
        $user = Auth::user($request);
        $pdo = Auth::pdo();
        $uuid = self::uuid($params);
        $data = $request->json();
        $base = isset($data['baseRevision']) && is_int($data['baseRevision']) && $data['baseRevision'] >= 0 ? $data['baseRevision'] : -1;
        $size = isset($data['size']) && is_int($data['size']) ? $data['size'] : 0;
        $checksum = strtolower($request->str('checksum'));
        if ($base < 0) {
            throw new HttpError(400, 'invalid_revision', 'Manca la revisione di partenza (0 per un salvataggio nuovo).');
        }
        if ($size < 1 || $size > self::MAX_SAVE_BYTES) {
            throw new HttpError(413, 'save_too_large', 'Salvataggio vuoto o troppo grande (massimo 64 MB).');
        }
        if (!preg_match('/^[0-9a-f]{64}$/', $checksum)) {
            throw new HttpError(400, 'invalid_checksum', 'Checksum SHA-256 non valido.');
        }
        $device = Auth::device($request);
        $meta = isset($data['meta']) && is_array($data['meta']) ? json_encode($data['meta'], JSON_UNESCAPED_UNICODE) : '{}';
        if (strlen($meta) > self::MAX_META_BYTES) {
            throw new HttpError(400, 'invalid_meta', 'Dati descrittivi troppo lunghi.');
        }
        $name = Auth::cleanText($request->str('name'), 60);
        $city = Auth::cleanText($request->str('cityName'), 60);

        $current = self::find($pdo, (int)$user['id'], $uuid, false);
        self::checkRevision($current, $base);
        if ($current === null) {
            $st = $pdo->prepare('SELECT COUNT(*) FROM orb_saves WHERE user_id = ?');
            $st->execute(array($user['id']));
            if ((int)$st->fetchColumn() >= self::MAX_SAVES) {
                throw new HttpError(409, 'too_many_saves', 'Hai già ' . self::MAX_SAVES . ' partite nel cloud: eliminane una.');
            }
        }
        self::cleanupUploads($pdo);
        $uploadId = bin2hex(random_bytes(16));
        $chunk = HealthController::chunkSize();
        $count = (int)ceil($size / $chunk);
        $pdo->prepare('INSERT INTO orb_save_uploads (upload_id, user_id, save_uuid, base_revision, size_bytes, chunk_count, checksum, name, city_name,
            device_id, device_name, meta, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, UTC_TIMESTAMP())')
            ->execute(array($uploadId, $user['id'], $uuid, $base, $size, $count, $checksum, $name === '' ? 'Partita' : $name, $city,
                $device['id'], $device['name'], $meta));
        Response::json(201, array('uploadId' => $uploadId, 'chunkSize' => $chunk, 'chunkCount' => $count));
    }

    public static function chunk(Request $request, array $params)
    {
        $user = Auth::user($request);
        $pdo = Auth::pdo();
        $upload = self::upload($pdo, (int)$user['id'], self::uuid($params), $params);
        $n = isset($params['n']) && ctype_digit($params['n']) ? (int)$params['n'] : -1;
        if ($n < 0 || $n >= (int)$upload['chunk_count']) {
            throw new HttpError(400, 'invalid_chunk', 'Numero del pezzo non valido.');
        }
        $chunk = HealthController::chunkSize();
        $expected = min($chunk, (int)$upload['size_bytes'] - $n * $chunk);
        $in = fopen('php://input', 'rb');
        $data = $in ? stream_get_contents($in, $expected + 1) : false;
        if ($in) {
            fclose($in);
        }
        if (!is_string($data) || strlen($data) !== $expected) {
            throw new HttpError(400, 'invalid_chunk', 'Il pezzo ' . $n . ' deve essere di ' . $expected . ' byte.');
        }
        $path = Storage::path('uploads/' . $upload['upload_id'] . '/' . $n . '.part');
        if (file_put_contents($path . '.tmp', $data) !== $expected || !rename($path . '.tmp', $path)) {
            throw new \RuntimeException('scrittura del pezzo non riuscita');
        }
        // REPLACE: un pezzo rimandato (rete caduta) sostituisce il precedente
        $pdo->prepare('REPLACE INTO orb_save_chunks (upload_id, chunk_index, size, checksum, created_at) VALUES (?, ?, ?, ?, UTC_TIMESTAMP())')
            ->execute(array($upload['upload_id'], $n, $expected, hash('sha256', $data)));
        Response::noContent();
    }

    public static function finish(Request $request, array $params)
    {
        $user = Auth::user($request);
        $pdo = Auth::pdo();
        $uuid = self::uuid($params);
        $upload = self::upload($pdo, (int)$user['id'], $uuid, $params);
        $dir = Storage::dir() . '/uploads/' . $upload['upload_id'];
        $st = $pdo->prepare('SELECT COUNT(*) FROM orb_save_chunks WHERE upload_id = ?');
        $st->execute(array($upload['upload_id']));
        if ((int)$st->fetchColumn() !== (int)$upload['chunk_count']) {
            throw new HttpError(409, 'upload_incomplete', 'Mancano dei pezzi del salvataggio.');
        }
        // pezzi uniti in un file temporaneo, controllando dimensione e checksum
        $tmp = Storage::path('saves/' . (int)$user['id'] . '/' . $uuid . '.' . $upload['upload_id'] . '.tmp');
        $out = fopen($tmp, 'wb');
        $hash = hash_init('sha256');
        $total = 0;
        for ($i = 0; $i < (int)$upload['chunk_count']; $i++) {
            $part = @file_get_contents($dir . '/' . $i . '.part');
            if (!is_string($part)) {
                fclose($out);
                @unlink($tmp);
                throw new HttpError(409, 'upload_incomplete', 'Manca il pezzo ' . $i . '.');
            }
            hash_update($hash, $part);
            fwrite($out, $part);
            $total += strlen($part);
        }
        fclose($out);
        if ($total !== (int)$upload['size_bytes'] || !hash_equals($upload['checksum'], hash_final($hash))) {
            @unlink($tmp);
            self::dropUpload($pdo, $upload['upload_id']);
            throw new HttpError(422, 'checksum_mismatch', 'Il salvataggio è arrivato rovinato: riprova.');
        }

        $pdo->beginTransaction();
        try {
            $st = $pdo->prepare('SELECT * FROM orb_saves WHERE user_id = ? AND save_uuid = ? FOR UPDATE');
            $st->execute(array($user['id'], $uuid));
            $current = $st->fetch();
            $current = $current ? $current : null;
            self::checkRevision($current, (int)$upload['base_revision']);
            $revision = (int)$upload['base_revision'] + 1;
            $relative = 'saves/' . (int)$user['id'] . '/' . $uuid . '.' . $revision . '.sav';
            if (!rename($tmp, Storage::path($relative))) {
                throw new \RuntimeException('salvataggio del file non riuscito');
            }
            $values = array($upload['name'], $upload['city_name'], $revision, $upload['device_id'], $upload['device_name'], $total, $upload['checksum'], $relative, $upload['meta']);
            if ($current === null) {
                $pdo->prepare('INSERT INTO orb_saves (name, city_name, revision, device_id, device_name, size_bytes, checksum, storage_path, meta, updated_at, user_id, save_uuid)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, UTC_TIMESTAMP(), ?, ?)')->execute(array_merge($values, array($user['id'], $uuid)));
            } else {
                $pdo->prepare('UPDATE orb_saves SET name = ?, city_name = ?, revision = ?, device_id = ?, device_name = ?, size_bytes = ?, checksum = ?, storage_path = ?, meta = ?,
                    updated_at = UTC_TIMESTAMP() WHERE id = ?')->execute(array_merge($values, array($current['id'])));
            }
            $pdo->commit();
        } catch (\Exception $e) {
            if ($pdo->inTransaction()) {
                $pdo->rollBack();
            }
            @unlink($tmp);
            if (isset($relative) && ($current === null || $current['storage_path'] !== $relative)) {
                @unlink(Storage::dir() . '/' . $relative);
            }
            self::dropUpload($pdo, $upload['upload_id']);
            // due dispositivi che creano insieme la stessa partita: il secondo trova il conflitto
            if ($e instanceof \PDOException && $e->getCode() === '23000') {
                self::checkRevision(self::find($pdo, (int)$user['id'], $uuid, false), (int)$upload['base_revision']);
            }
            throw $e;
        }
        // la revisione vecchia non serve più
        if ($current !== null && $current['storage_path'] !== $relative) {
            @unlink(Storage::dir() . '/' . $current['storage_path']);
        }
        self::dropUpload($pdo, $upload['upload_id']);
        Response::json(200, array('save' => self::meta(self::find($pdo, (int)$user['id'], $uuid, true))));
    }

    public static function download(Request $request, array $params)
    {
        $user = Auth::user($request);
        $row = self::find(Auth::pdo(), (int)$user['id'], self::uuid($params), true);
        $path = Storage::dir() . '/' . $row['storage_path'];
        $size = is_file($path) ? filesize($path) : false;
        if ($size === false) {
            throw new HttpError(404, 'not_found', 'File del salvataggio non trovato.');
        }
        $start = 0;
        $end = $size - 1;
        $range = $request->header('range');
        if ($range !== '') {
            if (!preg_match('/^bytes=(\d+)-(\d*)$/', trim($range), $m) || (int)$m[1] >= $size) {
                throw new HttpError(416, 'invalid_range', 'Intervallo non valido.', array('Content-Range: bytes */' . $size));
            }
            $start = (int)$m[1];
            $end = $m[2] === '' ? $size - 1 : min((int)$m[2], $size - 1);
            if ($end < $start) {
                throw new HttpError(416, 'invalid_range', 'Intervallo non valido.', array('Content-Range: bytes */' . $size));
            }
            http_response_code(206);
            header('Content-Range: bytes ' . $start . '-' . $end . '/' . $size);
        } else {
            http_response_code(200);
        }
        header('Content-Type: application/octet-stream');
        header('Content-Length: ' . ($end - $start + 1));
        header('Accept-Ranges: bytes');
        header('Cache-Control: no-store');
        header('ETag: "' . $row['checksum'] . '"');
        header('X-Orbis-Revision: ' . (int)$row['revision']);
        $f = fopen($path, 'rb');
        fseek($f, $start);
        $left = $end - $start + 1;
        while ($left > 0 && !feof($f)) {
            $buf = fread($f, min(65536, $left));
            if ($buf === false || $buf === '') {
                break;
            }
            echo $buf;
            $left -= strlen($buf);
        }
        fclose($f);
    }

    public static function delete(Request $request, array $params)
    {
        $user = Auth::user($request);
        $pdo = Auth::pdo();
        $row = self::find($pdo, (int)$user['id'], self::uuid($params), true);
        $pdo->prepare('DELETE FROM orb_saves WHERE id = ?')->execute(array($row['id']));
        @unlink(Storage::dir() . '/' . $row['storage_path']);
        Response::noContent();
    }

    // ------------------------------------------------------------------ utilità

    private static function uuid(array $params)
    {
        $uuid = isset($params['uuid']) ? strtolower($params['uuid']) : '';
        if (!preg_match(self::UUID, $uuid)) {
            throw new HttpError(400, 'invalid_uuid', 'Identificativo del salvataggio non valido.');
        }
        return $uuid;
    }

    private static function find(PDO $pdo, $userId, $uuid, $required)
    {
        $st = $pdo->prepare('SELECT * FROM orb_saves WHERE user_id = ? AND save_uuid = ?');
        $st->execute(array($userId, $uuid));
        $row = $st->fetch();
        if (!$row && $required) {
            throw new HttpError(404, 'not_found', 'Salvataggio non trovato nel cloud.');
        }
        return $row ? $row : null;
    }

    /** Conflitto se nel cloud c'è una revisione diversa da quella da cui è partito il dispositivo. */
    private static function checkRevision($current, $base)
    {
        $revision = $current === null ? 0 : (int)$current['revision'];
        if ($revision !== $base) {
            throw new HttpError(409, 'conflict', 'Nel cloud c\'è una versione diversa di questa partita.', array(),
                array('cloud' => $current === null ? null : self::meta($current)));
        }
    }

    private static function upload(PDO $pdo, $userId, $uuid, array $params)
    {
        $id = isset($params['uploadId']) ? $params['uploadId'] : '';
        if (!preg_match('/^[0-9a-f]{32}$/', $id)) {
            throw new HttpError(404, 'upload_not_found', 'Upload inesistente o scaduto.');
        }
        $st = $pdo->prepare('SELECT * FROM orb_save_uploads WHERE upload_id = ? AND user_id = ? AND save_uuid = ?');
        $st->execute(array($id, $userId, $uuid));
        $row = $st->fetch();
        if (!$row) {
            throw new HttpError(404, 'upload_not_found', 'Upload inesistente o scaduto.');
        }
        return $row;
    }

    private static function dropUpload(PDO $pdo, $uploadId)
    {
        $pdo->prepare('DELETE FROM orb_save_chunks WHERE upload_id = ?')->execute(array($uploadId));
        $pdo->prepare('DELETE FROM orb_save_uploads WHERE upload_id = ?')->execute(array($uploadId));
        Storage::removeDir(Storage::dir() . '/uploads/' . $uploadId);
    }

    /** Upload abbandonati da più di un giorno. */
    private static function cleanupUploads(PDO $pdo)
    {
        if (mt_rand(1, 20) !== 1) {
            return;
        }
        $old = $pdo->query('SELECT upload_id FROM orb_save_uploads WHERE created_at < UTC_TIMESTAMP() - INTERVAL 1 DAY LIMIT 50')->fetchAll(PDO::FETCH_COLUMN);
        foreach ($old as $id) {
            self::dropUpload($pdo, $id);
        }
    }

    private static function meta(array $row)
    {
        $meta = json_decode($row['meta'], true);
        return array(
            'uuid' => $row['save_uuid'],
            'name' => $row['name'],
            'cityName' => $row['city_name'],
            'revision' => (int)$row['revision'],
            'deviceId' => $row['device_id'],
            'deviceName' => $row['device_name'],
            'sizeBytes' => (int)$row['size_bytes'],
            'checksum' => $row['checksum'],
            'updatedAt' => Auth::iso($row['updated_at']),
            'meta' => is_array($meta) ? (object)$meta : new \stdClass(),
        );
    }
}
