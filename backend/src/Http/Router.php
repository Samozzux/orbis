<?php
namespace Orbis\Api\Http;

class Router
{
    private $routes = array();

    /** @param callable $handler riceve (Request $request, array $params) */
    public function add($method, $pattern, $handler)
    {
        $regex = '#^' . preg_replace('#\{([a-zA-Z_]+)\}#', '(?P<$1>[^/]+)', $pattern) . '$#';
        $this->routes[] = array('method' => $method, 'regex' => $regex, 'handler' => $handler);
    }

    public function dispatch(Request $request)
    {
        $pathMatched = false;
        foreach ($this->routes as $route) {
            if (!preg_match($route['regex'], $request->path, $m)) {
                continue;
            }
            $pathMatched = true;
            if ($route['method'] !== $request->method) {
                continue;
            }
            $params = array();
            foreach ($m as $k => $v) {
                if (is_string($k)) {
                    $params[$k] = $v;
                }
            }
            call_user_func($route['handler'], $request, $params);
            return;
        }
        if ($pathMatched) {
            Response::error(405, 'method_not_allowed', 'Metodo non ammesso per questa risorsa.');
        } else {
            Response::error(404, 'not_found', 'Risorsa inesistente.');
        }
    }
}
