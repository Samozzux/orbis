<?php
namespace Orbis\Api\Auth;

use Orbis\Api\Http\HttpError;

/**
 * Lettore CBOR minimo (RFC 8949) per WebAuthn: interi, stringhe di byte e di testo, array, mappe, booleani,
 * null. Le stringhe di byte tornano come stringhe PHP binarie. Niente float a precisione variabile, niente
 * lunghezze indefinite (gli autenticatori non le usano negli oggetti di WebAuthn).
 */
class Cbor
{
    /** Decodifica il primo elemento da $offset; restituisce array(valore, offset dopo l'elemento). */
    public static function decode($data, $offset = 0)
    {
        $len = strlen($data);
        if ($offset >= $len) {
            throw self::error();
        }
        $initial = ord($data[$offset]);
        $offset++;
        $major = $initial >> 5;
        $info = $initial & 0x1f;
        if ($major === 7) {
            if ($info === 20) return array(false, $offset);
            if ($info === 21) return array(true, $offset);
            if ($info === 22 || $info === 23) return array(null, $offset);
            throw self::error();
        }
        list($arg, $offset) = self::argument($data, $offset, $info);
        switch ($major) {
            case 0:
                return array($arg, $offset);
            case 1:
                return array(-1 - $arg, $offset);
            case 2:
            case 3:
                if ($offset + $arg > $len) {
                    throw self::error();
                }
                return array(substr($data, $offset, $arg), $offset + $arg);
            case 4:
                $out = array();
                for ($i = 0; $i < $arg; $i++) {
                    list($v, $offset) = self::decode($data, $offset);
                    $out[] = $v;
                }
                return array($out, $offset);
            case 5:
                $out = array();
                for ($i = 0; $i < $arg; $i++) {
                    list($k, $offset) = self::decode($data, $offset);
                    list($v, $offset) = self::decode($data, $offset);
                    if (!is_int($k) && !is_string($k)) {
                        throw self::error();
                    }
                    $out[$k] = $v;
                }
                return array($out, $offset);
            default:
                // tag (6): si salta l'etichetta e si legge il valore
                return self::decode($data, $offset);
        }
    }

    private static function argument($data, $offset, $info)
    {
        if ($info < 24) {
            return array($info, $offset);
        }
        $sizes = array(24 => 1, 25 => 2, 26 => 4, 27 => 8);
        if (!isset($sizes[$info]) || $offset + $sizes[$info] > strlen($data)) {
            throw self::error();
        }
        $n = 0;
        for ($i = 0; $i < $sizes[$info]; $i++) {
            $n = $n * 256 + ord($data[$offset + $i]);
        }
        return array($n, $offset + $sizes[$info]);
    }

    private static function error()
    {
        return new HttpError(400, 'invalid_passkey', 'Dati della passkey non validi.');
    }
}
