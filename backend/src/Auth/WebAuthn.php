<?php
namespace Orbis\Api\Auth;

use Orbis\Api\Http\Cors;
use Orbis\Api\Http\HttpError;

/**
 * Verifiche WebAuthn per le passkey della versione web (docs/06 §4): dati del client (tipo, sfida, origine),
 * dati dell'autenticatore (hash dell'RP ID, presenza e verifica dell'utente, contatore), chiavi COSE
 * ES256 e RS256 convertite in PEM per openssl, firma su authenticatorData || SHA-256(clientDataJSON).
 * L'attestazione non si verifica (le opzioni chiedono "none"): conta la chiave, non il modello del dispositivo.
 */
class WebAuthn
{
    const ES256 = -7;
    const RS256 = -257;
    const FLAG_UP = 0x01;
    const FLAG_UV = 0x04;
    const FLAG_AT = 0x40;

    public static function b64($bin)
    {
        return rtrim(strtr(base64_encode($bin), '+/', '-_'), '=');
    }

    public static function unb64($text)
    {
        if (!is_string($text) || !preg_match('/^[A-Za-z0-9_-]*$/', $text)) {
            throw self::error('Codifica non valida.');
        }
        $pad = strlen($text) % 4;
        $raw = base64_decode(strtr($text, '-_', '+/') . ($pad ? str_repeat('=', 4 - $pad) : ''), true);
        if ($raw === false) {
            throw self::error('Codifica non valida.');
        }
        return $raw;
    }

    /** RP ID per l'origine della pagina: l'host (es. samozzux.github.io, localhost in sviluppo). */
    public static function rpIdFor($origin)
    {
        if (!Cors::isAllowed($origin)) {
            return null;
        }
        $host = parse_url($origin, PHP_URL_HOST);
        return is_string($host) ? trim($host, '[]') : null;
    }

    /**
     * Controlla clientDataJSON: tipo ("webauthn.create" o "webauthn.get"), sfida, origine ammessa.
     * Restituisce l'origine (da cui si ricava l'RP ID).
     */
    public static function clientData($json, $type, $challenge)
    {
        $data = json_decode($json, true);
        if (!is_array($data) || !isset($data['type'], $data['challenge'], $data['origin'])) {
            throw self::error('Dati del browser non validi.');
        }
        if ($data['type'] !== $type) {
            throw self::error('Operazione della passkey non valida.');
        }
        if (!is_string($data['challenge']) || !hash_equals($challenge, $data['challenge'])) {
            throw self::error('Sfida della passkey non valida.');
        }
        if (self::rpIdFor($data['origin']) === null) {
            throw self::error('Origine della pagina non ammessa.');
        }
        return $data['origin'];
    }

    /**
     * Legge authenticatorData: hash dell'RP ID, flag, contatore e (in registrazione) la credenziale con la chiave COSE.
     * Richiede la presenza (UP) e la verifica dell'utente (UV: Face ID, Touch ID, Windows Hello, PIN).
     */
    public static function authData($bin, $rpId, $withCredential)
    {
        if (strlen($bin) < 37) {
            throw self::error('Dati dell\'autenticatore troppo corti.');
        }
        if (!hash_equals(hash('sha256', $rpId, true), substr($bin, 0, 32))) {
            throw self::error('La passkey è di un altro sito.');
        }
        $flags = ord($bin[32]);
        if (($flags & self::FLAG_UP) === 0 || ($flags & self::FLAG_UV) === 0) {
            throw self::error('Serve lo sblocco del dispositivo per usare la passkey.');
        }
        $u = unpack('N', substr($bin, 33, 4));
        $out = array('flags' => $flags, 'signCount' => $u[1]);
        if (!$withCredential) {
            return $out;
        }
        if (($flags & self::FLAG_AT) === 0 || strlen($bin) < 55) {
            throw self::error('Manca la credenziale nella passkey.');
        }
        $l = unpack('n', substr($bin, 53, 2));
        $idLen = $l[1];
        if ($idLen < 1 || $idLen > 191 || strlen($bin) < 55 + $idLen) {
            throw self::error('Identificativo della passkey non valido.');
        }
        $out['credentialId'] = substr($bin, 55, $idLen);
        list($cose, $end) = Cbor::decode($bin, 55 + $idLen);
        if (!is_array($cose)) {
            throw self::error('Chiave della passkey non valida.');
        }
        $out['cose'] = $cose;
        return $out;
    }

    /** Chiave COSE → array(PEM della chiave pubblica, algoritmo). Supporta ES256 (P-256) e RS256. */
    public static function coseToPem(array $cose)
    {
        $kty = isset($cose[1]) ? $cose[1] : null;
        $alg = isset($cose[3]) ? $cose[3] : null;
        if ($kty === 2 && $alg === self::ES256) {
            $crv = isset($cose[-1]) ? $cose[-1] : null;
            $x = isset($cose[-2]) ? $cose[-2] : '';
            $y = isset($cose[-3]) ? $cose[-3] : '';
            if ($crv !== 1 || !is_string($x) || !is_string($y) || strlen($x) !== 32 || strlen($y) !== 32) {
                throw self::error('Chiave ES256 non valida.');
            }
            // SubjectPublicKeyInfo: id-ecPublicKey + prime256v1, punto non compresso
            $der = hex2bin('3059301306072a8648ce3d020106082a8648ce3d030107034200') . "\x04" . $x . $y;
            return array(self::pem($der), self::ES256);
        }
        if ($kty === 3 && $alg === self::RS256) {
            $n = isset($cose[-1]) ? $cose[-1] : '';
            $e = isset($cose[-2]) ? $cose[-2] : '';
            if (!is_string($n) || !is_string($e) || strlen($n) < 256 || strlen($e) < 1) {
                throw self::error('Chiave RS256 non valida.');
            }
            $rsa = self::der(0x30, self::der(0x02, self::unsigned($n)) . self::der(0x02, self::unsigned($e)));
            $algorithm = self::der(0x30, hex2bin('06092a864886f70d0101010500'));
            $der = self::der(0x30, $algorithm . self::der(0x03, "\x00" . $rsa));
            return array(self::pem($der), self::RS256);
        }
        throw self::error('Tipo di passkey non supportato.');
    }

    /** Firma su authenticatorData || SHA-256(clientDataJSON) (DER per ES256, PKCS#1 v1.5 per RS256). */
    public static function verifySignature($authData, $clientDataJson, $signature, $pem)
    {
        $key = openssl_pkey_get_public($pem);
        if ($key === false) {
            throw self::error('Chiave della passkey illeggibile.');
        }
        return openssl_verify($authData . hash('sha256', $clientDataJson, true), $signature, $key, OPENSSL_ALGO_SHA256) === 1;
    }

    private static function pem($der)
    {
        return "-----BEGIN PUBLIC KEY-----\n" . chunk_split(base64_encode($der), 64, "\n") . "-----END PUBLIC KEY-----\n";
    }

    private static function unsigned($bytes)
    {
        $bytes = ltrim($bytes, "\x00");
        if ($bytes === '' || ord($bytes[0]) & 0x80) {
            $bytes = "\x00" . $bytes;
        }
        return $bytes;
    }

    private static function der($tag, $content)
    {
        $len = strlen($content);
        if ($len < 0x80) {
            $l = chr($len);
        } else {
            $bytes = '';
            while ($len > 0) {
                $bytes = chr($len & 0xff) . $bytes;
                $len >>= 8;
            }
            $l = chr(0x80 | strlen($bytes)) . $bytes;
        }
        return chr($tag) . $l . $content;
    }

    private static function error($message)
    {
        return new HttpError(400, 'invalid_passkey', $message);
    }
}
