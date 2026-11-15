<?php
namespace Orbis\Api\Http;

class Response
{
    public static function json($status, $data)
    {
        http_response_code($status);
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        header('X-Content-Type-Options: nosniff');
        echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_PRETTY_PRINT);
    }

    public static function error($status, $code, $message)
    {
        self::json($status, array('error' => array('code' => $code, 'message' => $message)));
    }

    public static function noContent()
    {
        http_response_code(204);
    }
}
