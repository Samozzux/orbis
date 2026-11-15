<?php
// Router per il server di sviluppo di PHP (php -S): emula il rewrite di api/.htaccess.
$path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
if (strpos($path, '/api') === 0) {
    $_SERVER['SCRIPT_NAME'] = '/api/index.php';
    require __DIR__ . '/../api/index.php';
    return true;
}
return false;
