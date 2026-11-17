<?php
namespace Orbis\Api\Controllers;

use Orbis\Api\Auth\Auth;
use Orbis\Api\Http\HttpError;
use Orbis\Api\Http\Request;
use Orbis\Api\Http\Response;

/** GET/PATCH /api/me (docs/06 §5): profilo dell'utente collegato. */
class MeController
{
    public static function show(Request $request, array $params)
    {
        $user = Auth::user($request);
        Response::json(200, array('user' => Auth::publicUser($user)));
    }

    public static function update(Request $request, array $params)
    {
        $user = Auth::user($request);
        $data = $request->json();
        if (array_key_exists('displayName', $data)) {
            $name = Auth::cleanText(is_scalar($data['displayName']) ? $data['displayName'] : '', 40);
            if ($name === '') {
                throw new HttpError(400, 'invalid_display_name', 'Il nome visualizzato non può essere vuoto.');
            }
            Auth::pdo()->prepare('UPDATE orb_users SET display_name = ? WHERE id = ?')->execute(array($name, $user['id']));
            $user['display_name'] = $name;
        }
        Response::json(200, array('user' => Auth::publicUser($user)));
    }
}
