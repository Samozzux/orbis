<?php
// Test minimi del backend (docs/06 §7). Uso:
//   php backend/tests/run.php
// Database facoltativo (in CI: MySQL 5.7 in container) tramite variabili d'ambiente:
//   ORBIS_TEST_DB_HOST, ORBIS_TEST_DB_PORT, ORBIS_TEST_DB_NAME, ORBIS_TEST_DB_USER, ORBIS_TEST_DB_PASSWORD
// Avvia il server di sviluppo di PHP, fa richieste HTTP vere e controlla le risposte.

error_reporting(E_ALL);
$root = dirname(__DIR__);
$hasDb = getenv('ORBIS_TEST_DB_NAME') !== false && getenv('ORBIS_TEST_DB_NAME') !== '';
$failures = 0;
$passed = 0;

function check($name, $condition, $detail = '')
{
    global $failures, $passed;
    if ($condition) {
        $passed++;
        echo "  ok   $name\n";
    } else {
        $failures++;
        echo "  FAIL $name" . ($detail !== '' ? " — $detail" : '') . "\n";
    }
}

function request($method, $path, $headers = array(), $body = null)
{
    global $base;
    $http = array(
        'method' => $method,
        'header' => implode("\r\n", $headers),
        'ignore_errors' => true,
        'timeout' => 10,
    );
    if ($body !== null) {
        $http['content'] = $body;
    }
    $context = stream_context_create(array('http' => $http));
    $body = @file_get_contents($base . $path, false, $context);
    $status = 0;
    $out = array();
    // PHP 8.4+: http_get_last_response_headers(); prima: $http_response_header
    $headers = function_exists('http_get_last_response_headers') ? http_get_last_response_headers() : (isset($http_response_header) ? $http_response_header : null);
    if (is_array($headers)) {
        foreach ($headers as $line) {
            if (preg_match('#^HTTP/\S+\s+(\d+)#', $line, $m)) {
                $status = (int)$m[1];
            } elseif (strpos($line, ':') !== false) {
                list($k, $v) = explode(':', $line, 2);
                $out[strtolower(trim($k))] = trim($v);
            }
        }
    }
    return array('status' => $status, 'headers' => $out, 'body' => $body, 'json' => json_decode((string)$body, true));
}

/** Chiamata JSON alle API, con token di accesso facoltativo. */
function api($method, $path, $data = null, $token = null)
{
    $headers = array('Origin: https://samozzux.github.io');
    if ($data !== null) {
        $headers[] = 'Content-Type: application/json';
    }
    if ($token !== null) {
        $headers[] = 'Authorization: Bearer ' . $token;
    }
    return request($method, $path, $headers, $data !== null ? json_encode($data) : null);
}

function errorCode($r)
{
    return isset($r['json']['error']['code']) ? $r['json']['error']['code'] : '(nessuno: ' . $r['status'] . ' ' . substr((string)$r['body'], 0, 200) . ')';
}

// configurazione temporanea
$storage = sys_get_temp_dir() . '/orbis-test-' . getmypid();
$configFile = $storage . '-config.php';
$config = array(
    'env' => 'test',
    'db' => $hasDb ? array(
        'host' => getenv('ORBIS_TEST_DB_HOST') ?: '127.0.0.1',
        'port' => (int)(getenv('ORBIS_TEST_DB_PORT') ?: 3306),
        'name' => getenv('ORBIS_TEST_DB_NAME'),
        'user' => getenv('ORBIS_TEST_DB_USER') ?: 'root',
        'password' => getenv('ORBIS_TEST_DB_PASSWORD') ?: '',
    ) : array(),
    'jwt_secret' => str_repeat('a', 64),
    'admin_username' => 'admin',
    'cors_origins' => array('https://samozzux.github.io'),
    'cors_allow_localhost' => true,
);
file_put_contents($configFile, '<?php return ' . var_export($config, true) . ';');
@unlink($root . '/storage/migrations.ok');
$testPdo = null;
if ($hasDb) {
    // si riparte da zero (anche per provare le migrazioni dall'inizio): solo tabelle orb_ del database di test
    $db = $config['db'];
    $testPdo = new PDO('mysql:host=' . $db['host'] . ';port=' . $db['port'] . ';dbname=' . $db['name'], $db['user'], $db['password'],
        array(PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC));
    foreach ($testPdo->query("SHOW TABLES LIKE 'orb\\_%'")->fetchAll(PDO::FETCH_COLUMN) as $table) {
        $testPdo->exec('DROP TABLE `' . $table . '`');
    }
}

$port = 18000 + (getmypid() % 1000);
$base = "http://127.0.0.1:$port";
$cmd = sprintf('ORBIS_CONFIG_FILE=%s %s -S 127.0.0.1:%d -t %s %s',
    escapeshellarg($configFile), escapeshellarg(PHP_BINARY), $port, escapeshellarg($root), escapeshellarg($root . '/tests/router.php'));
$server = proc_open($cmd, array(1 => array('file', '/dev/null', 'w'), 2 => array('file', '/dev/null', 'w')), $pipes);
for ($i = 0; $i < 50; $i++) {
    $fp = @fsockopen('127.0.0.1', $port);
    if ($fp) {
        fclose($fp);
        break;
    }
    usleep(100000);
}

echo "Orbis backend — test (PHP " . PHP_VERSION . ", database: " . ($hasDb ? 'sì' : 'no') . ")\n";

try {
    // --- health ---
    $r = request('GET', '/api/health', array('Origin: https://samozzux.github.io'));
    check('health risponde JSON', is_array($r['json']), $r['body']);
    check('health contiene la versione PHP', isset($r['json']['php']['version']) && $r['json']['php']['version'] === PHP_VERSION);
    check('health elenca le estensioni', isset($r['json']['extensions']['openssl']) && isset($r['json']['extensions']['pdo_mysql']));
    check('health riporta i limiti', isset($r['json']['ini']['upload_max_filesize']) && $r['json']['limits']['chunkSizeBytes'] > 0);
    check('health non espone segreti', strpos((string)$r['body'], str_repeat('a', 64)) === false);
    check('health: segreto JWT presente', isset($r['json']['config']['jwtSecret']) && $r['json']['config']['jwtSecret'] === true);
    if ($hasDb) {
        check('health 200 con database', $r['status'] === 200, 'status ' . $r['status']);
        check('database connesso', $r['json']['database']['connected'] === true, json_encode($r['json']['database']));
        check('migrazioni applicate', $r['json']['database']['migrations']['pending'] === 0 && $r['json']['database']['migrations']['applied'] >= 1);
        $r2 = request('GET', '/api/health');
        check('migrazioni idempotenti', $r2['status'] === 200 && $r2['json']['database']['migrations']['applied'] === $r['json']['database']['migrations']['applied']);
        $c = request('GET', '/api/config');
        check('config remota', $c['status'] === 200 && isset($c['json']['config']['min_app_version']), $c['body']);
    } else {
        check('health 503 senza database', $r['status'] === 503 && $r['json']['status'] === 'degraded');
    }

    // --- CORS ---
    check('CORS: origine del gioco web ammessa', isset($r['headers']['access-control-allow-origin']) && $r['headers']['access-control-allow-origin'] === 'https://samozzux.github.io');
    $r = request('GET', '/api/health', array('Origin: https://esempio-malevolo.test'));
    check('CORS: altre origini rifiutate', !isset($r['headers']['access-control-allow-origin']));
    $r = request('GET', '/api/health', array('Origin: http://localhost:8080'));
    check('CORS: localhost ammesso per sviluppo', isset($r['headers']['access-control-allow-origin']) && $r['headers']['access-control-allow-origin'] === 'http://localhost:8080');
    $r = request('OPTIONS', '/api/health', array('Origin: https://samozzux.github.io', 'Access-Control-Request-Method: GET', 'Access-Control-Request-Headers: authorization'));
    check('CORS: preflight 204', $r['status'] === 204, 'status ' . $r['status']);
    check('CORS: preflight ammette Authorization', isset($r['headers']['access-control-allow-headers']) && stripos($r['headers']['access-control-allow-headers'], 'authorization') !== false);

    // --- router ---
    $r = request('GET', '/api/non-esiste');
    check('404 per rotte inesistenti', $r['status'] === 404 && $r['json']['error']['code'] === 'not_found');
    $r = request('POST', '/api/health');
    check('405 per metodo non ammesso', $r['status'] === 405);
    $r = request('GET', '/api/index.php?route=health');
    check('rotta alternativa ?route=', isset($r['json']['service']) && $r['json']['service'] === 'orbis-api');
    $r = request('GET', '/api/index.php/health');
    check('rotta alternativa PATH_INFO', isset($r['json']['service']) && $r['json']['service'] === 'orbis-api');

    // --- migrazioni: separazione delle istruzioni ---
    require_once $root . '/src/bootstrap.php';
    $parts = \Orbis\Api\Migrator::splitStatements("-- commento\nCREATE TABLE orb_a (x INT);\n\nINSERT INTO orb_a VALUES (1);\n");
    check('split delle istruzioni SQL', count($parts) === 2 && strpos($parts[0], 'CREATE TABLE orb_a') === 0);

    // --- JWT ---
    $secret = str_repeat('a', 64);
    $tok = \Orbis\Api\Auth\Jwt::encode(array('sub' => '7', 'exp' => time() + 60), $secret);
    $claims = \Orbis\Api\Auth\Jwt::decode($tok, $secret);
    check('JWT: firma e lettura', is_array($claims) && $claims['sub'] === '7');
    check('JWT: segreto sbagliato rifiutato', \Orbis\Api\Auth\Jwt::decode($tok, str_repeat('b', 64)) === null);
    check('JWT: scaduto rifiutato', \Orbis\Api\Auth\Jwt::decode(\Orbis\Api\Auth\Jwt::encode(array('sub' => '7', 'exp' => time() - 1), $secret), $secret) === null);
    $parts = explode('.', $tok);
    $none = \Orbis\Api\Auth\Jwt::b64(json_encode(array('alg' => 'none', 'typ' => 'JWT'))) . '.' . $parts[1] . '.';
    check('JWT: alg none rifiutato', \Orbis\Api\Auth\Jwt::decode($none, $secret) === null);
    $forged = $parts[0] . '.' . \Orbis\Api\Auth\Jwt::b64(json_encode(array('sub' => '1', 'exp' => time() + 60))) . '.' . $parts[2];
    check('JWT: contenuto alterato rifiutato', \Orbis\Api\Auth\Jwt::decode($forged, $secret) === null);

    if ($hasDb) {
        require __DIR__ . '/auth.php';
    }
} finally {
    proc_terminate($server);
    proc_close($server);
    @unlink($configFile);
    @unlink($root . '/storage/migrations.ok');
}

echo "\n$passed superati, $failures falliti\n";
exit($failures > 0 ? 1 : 0);
