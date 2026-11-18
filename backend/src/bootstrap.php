<?php
// Avvio comune: autoload delle classi Orbis\Api\* da src/ e costanti.
// Vincoli Altervista (CLAUDE.md): PHP 7.x, niente fn(), match, ?->, proprietà tipizzate.

define('ORBIS_API_VERSION', '0.4.0');
define('ORBIS_ROOT', dirname(__DIR__));

spl_autoload_register(function ($class) {
    $prefix = 'Orbis\\Api\\';
    if (strpos($class, $prefix) !== 0) {
        return;
    }
    $relative = str_replace('\\', '/', substr($class, strlen($prefix)));
    $file = __DIR__ . '/' . $relative . '.php';
    if (is_file($file)) {
        require $file;
    }
});
