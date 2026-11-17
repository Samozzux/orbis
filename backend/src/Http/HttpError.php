<?php
namespace Orbis\Api\Http;

/** Errore da restituire al client: lo trasforma in risposta JSON App::run (codice + messaggio). */
class HttpError extends \Exception
{
    public $status;
    public $errorCode;
    public $headers;

    public function __construct($status, $errorCode, $message, array $headers = array())
    {
        parent::__construct($message);
        $this->status = $status;
        $this->errorCode = $errorCode;
        $this->headers = $headers;
    }
}
