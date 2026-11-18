<?php
namespace Orbis\Api\Http;

/** Errore da restituire al client: lo trasforma in risposta JSON App::run (codice + messaggio). */
class HttpError extends \Exception
{
    public $status;
    public $errorCode;
    public $headers;
    /** campi in più nella risposta (es. i metadati della versione cloud in un conflitto) */
    public $extra;

    public function __construct($status, $errorCode, $message, array $headers = array(), array $extra = array())
    {
        parent::__construct($message);
        $this->status = $status;
        $this->errorCode = $errorCode;
        $this->headers = $headers;
        $this->extra = $extra;
    }
}
