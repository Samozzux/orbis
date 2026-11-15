<?php
namespace Orbis\Api\Controllers;

use Orbis\Api\Db;
use Orbis\Api\Http\Request;
use Orbis\Api\Http\Response;

/** GET /api/config (docs/06 §5): configurazione remota letta da orb_app_config. */
class ConfigController
{
    public static function show(Request $request, array $params)
    {
        $pdo = Db::connection();
        if ($pdo === null) {
            Response::error(503, 'database_unavailable', 'Database non raggiungibile.');
            return;
        }
        $config = array();
        foreach ($pdo->query('SELECT `key`, `value` FROM orb_app_config ORDER BY `key`') as $row) {
            $config[$row['key']] = $row['value'];
        }
        Response::json(200, array('config' => $config));
    }
}
