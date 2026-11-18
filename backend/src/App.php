<?php
namespace Orbis\Api;

use Orbis\Api\Http\Cors;
use Orbis\Api\Http\HttpError;
use Orbis\Api\Http\Request;
use Orbis\Api\Http\Response;
use Orbis\Api\Http\Router;

class App
{
    public static function run()
    {
        $request = Request::fromGlobals();
        Cors::apply($request->origin);

        if ($request->method === 'OPTIONS') {
            Response::noContent(); // preflight CORS
            return;
        }

        try {
            self::migrate();
            $router = new Router();
            $router->add('GET', '/', function () {
                Response::json(200, array('service' => 'orbis-api', 'apiVersion' => ORBIS_API_VERSION));
            });
            $router->add('GET', '/health', array('Orbis\Api\Controllers\HealthController', 'health'));
            $router->add('GET', '/config', array('Orbis\Api\Controllers\ConfigController', 'show'));
            // account (M3, docs/06 §4)
            $auth = 'Orbis\Api\Controllers\AuthController';
            $router->add('POST', '/auth/register', array($auth, 'register'));
            $router->add('POST', '/auth/login', array($auth, 'login'));
            $router->add('POST', '/auth/refresh', array($auth, 'refresh'));
            $router->add('POST', '/auth/logout', array($auth, 'logout'));
            $router->add('GET', '/auth/sessions', array($auth, 'sessions'));
            $router->add('DELETE', '/auth/sessions/{id}', array($auth, 'revokeSession'));
            $router->add('GET', '/me', array('Orbis\Api\Controllers\MeController', 'show'));
            $router->add('PATCH', '/me', array('Orbis\Api\Controllers\MeController', 'update'));
            // salvataggi nel cloud (M3, docs/06 §5)
            $saves = 'Orbis\Api\Controllers\SavesController';
            $router->add('GET', '/saves', array($saves, 'index'));
            $router->add('GET', '/saves/{uuid}', array($saves, 'show'));
            $router->add('DELETE', '/saves/{uuid}', array($saves, 'delete'));
            $router->add('GET', '/saves/{uuid}/download', array($saves, 'download'));
            $router->add('POST', '/saves/{uuid}/upload/start', array($saves, 'start'));
            $router->add('PUT', '/saves/{uuid}/upload/{uploadId}/chunk/{n}', array($saves, 'chunk'));
            $router->add('POST', '/saves/{uuid}/upload/{uploadId}/finish', array($saves, 'finish'));
            $router->dispatch($request);
        } catch (HttpError $e) {
            foreach ($e->headers as $header) {
                header($header);
            }
            Response::json($e->status, array_merge(array('error' => array('code' => $e->errorCode, 'message' => $e->getMessage())), $e->extra));
        } catch (\Throwable $e) {
            error_log('[orbis] errore: ' . $e->getMessage());
            Response::error(500, 'server_error', 'Errore interno.');
        }
    }

    /** Applica le migrazioni mancanti (se il DB è raggiungibile). */
    private static function migrate()
    {
        $pdo = Db::connection();
        if ($pdo === null) {
            return;
        }
        $migrator = new Migrator($pdo, ORBIS_ROOT . '/migrations', ORBIS_ROOT . '/storage');
        $migrator->migrate();
    }
}
