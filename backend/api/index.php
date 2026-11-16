<?php
// Punto di ingresso unico delle API di Orbis (docs/06 §2). Compatibile PHP 7.0+.
require __DIR__ . '/../src/bootstrap.php';

use Orbis\Api\App;

App::run();
