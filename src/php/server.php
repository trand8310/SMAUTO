<?php

use Swoole\WebSocket\Server;
use Swoole\Http\Request;
use Swoole\Http\Response;
use Swoole\WebSocket\Frame;

$server = new Server("0.0.0.0", 9502);

// ===== 基础配置 =====
$serverToken = 'abc123';

// ===== Redis 配置 =====
$redisHost = '127.0.0.1';
$redisPort = 6379;
$redisAuth = 'p86JEZzrl2ebn6Y0';
$redisPrefix = 'wsctl:';

// 客户端信息保留秒数（心跳会续期）
$clientInfoExpire = 120;

// 默认请求结果保留秒数
$requestExpire = 600;

// 每个客户端最近请求最多保留多少条
$clientRecentRequestLimit = 100;

// 截图文件保留秒数（Redis 中 file token 的有效期）
$screenshotTokenExpire = 1800;

// 截图保存目录
$screenshotDir = __DIR__ . '/runtime/ws_screenshots';
if (!is_dir($screenshotDir)) {
    @mkdir($screenshotDir, 0777, true);
}

$server->set([
    'worker_num' => 4,
    'daemonize' => false,
    'max_request' => 0,
    'dispatch_mode' => 2,
]);

function getRedis(string $host, int $port, string $auth): Redis
{
    static $redis = null;

    try {
        if ($redis instanceof Redis) {
            $redis->ping();
            return $redis;
        }
    } catch (Throwable $e) {
        $redis = null;
    }

    $redis = new Redis();
    $redis->connect($host, $port);
    if ($auth !== '') {
        $redis->auth($auth);
    }
    return $redis;
}

function jsonResponse(Response $response, array $data, int $status = 200): void
{
    $response->status($status);
    $response->header('Content-Type', 'application/json; charset=utf-8');
    $response->end(json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
}

function readJsonBody(Request $request): array
{
    $raw = $request->rawContent();
    if (!$raw) {
        return [];
    }

    $data = json_decode($raw, true);
    return is_array($data) ? $data : [];
}

function redisKey(string $prefix, string $key): string
{
    return $prefix . $key;
}

function nowTime(): int
{
    return time();
}

function getClientInfo(Redis $redis, string $prefix, string $clientId): ?array
{
    $key = redisKey($prefix, "client:{$clientId}");
    $raw = $redis->get($key);
    if ($raw === false || $raw === null || $raw === '') {
        return null;
    }

    $data = json_decode($raw, true);
    return is_array($data) ? $data : null;
}

function saveClientInfo(
    Redis $redis,
    string $prefix,
    string $clientId,
    int $fd,
    int $expireSeconds,
    array $extra = []
): void {
    $key = redisKey($prefix, "client:{$clientId}");
    $fdKey = redisKey($prefix, "fd:{$fd}");
    $onlineSetKey = redisKey($prefix, "clients:online");

    $now = nowTime();

    $old = getClientInfo($redis, $prefix, $clientId);
    if ($old && isset($old['fd'])) {
        $oldFd = (int)$old['fd'];
        if ($oldFd > 0 && $oldFd !== $fd) {
            $oldFdKey = redisKey($prefix, "fd:{$oldFd}");
            $redis->del($oldFdKey);
        }
    }

    $connectedAt = $old['connectedAt'] ?? $now;

    $info = array_merge([
        'clientId' => $clientId,
        'fd' => $fd,
        'connectedAt' => (int)$connectedAt,
        'lastHeartbeat' => $now,
        'updatedAt' => $now,
    ], $extra);

    $redis->set($key, json_encode($info, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES), $expireSeconds);
    $redis->set($fdKey, $clientId, $expireSeconds);
    $redis->sAdd($onlineSetKey, $clientId);
}

function removeClientById(Redis $redis, string $prefix, string $clientId): void
{
    $key = redisKey($prefix, "client:{$clientId}");
    $onlineSetKey = redisKey($prefix, "clients:online");

    $info = getClientInfo($redis, $prefix, $clientId);
    if ($info && isset($info['fd'])) {
        $fdKey = redisKey($prefix, "fd:" . (int)$info['fd']);
        $redis->del($fdKey);
    }

    $redis->del($key);
    $redis->sRem($onlineSetKey, $clientId);
}

function removeClientByFd(Redis $redis, string $prefix, int $fd): ?string
{
    $fdKey = redisKey($prefix, "fd:{$fd}");
    $clientId = $redis->get($fdKey);

    if ($clientId === false || $clientId === null || $clientId === '') {
        return null;
    }

    $clientId = (string)$clientId;
    $info = getClientInfo($redis, $prefix, $clientId);

    // 旧 fd 的 close，不应把当前新连接删掉
    if ($info && (int)($info['fd'] ?? 0) !== $fd) {
        $redis->del($fdKey);
        return $clientId;
    }

    removeClientById($redis, $prefix, $clientId);
    return $clientId;
}

function updateClientHeartbeat(
    Redis $redis,
    string $prefix,
    string $clientId,
    int $expireSeconds
): void {
    $key = redisKey($prefix, "client:{$clientId}");
    $raw = $redis->get($key);
    if ($raw === false || $raw === null || $raw === '') {
        return;
    }

    $data = json_decode($raw, true);
    if (!is_array($data)) {
        return;
    }

    $data['lastHeartbeat'] = nowTime();
    $data['updatedAt'] = nowTime();

    $redis->set($key, json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES), $expireSeconds);

    if (isset($data['fd'])) {
        $fdKey = redisKey($prefix, "fd:" . (int)$data['fd']);
        $redis->set($fdKey, $clientId, $expireSeconds);
    }

    $onlineSetKey = redisKey($prefix, "clients:online");
    $redis->sAdd($onlineSetKey, $clientId);
}

function getActionTimeoutSeconds(string $action, array $requestData = []): int
{
    if ($action === 'get_config' || $action === 'set_config') {
        return 30;
    }

    if ($action === 'show_message') {
        return 30;
    }

    if ($action === 'command') {
        $command = trim((string)($requestData['payload']['command'] ?? ''));

        if ($command === 'screen_screenshot' || $command === 'app_screenshot') {
            return 60;
        }

        if ($command === 'machine_restart' || $command === 'machine_logoff') {
            return 15;
        }

        if ($command === 'app_start' || $command === 'app_stop') {
            return 30;
        }

        return 30;
    }

    return 30;
}

function getRequestExpireSeconds(string $action, array $requestData = [], int $defaultExpire = 600): int
{
    if ($action === 'command') {
        $command = trim((string)($requestData['payload']['command'] ?? ''));
        if ($command === 'screen_screenshot' || $command === 'app_screenshot') {
            return 120;
        }
    }

    return $defaultExpire;
}

function saveRequest(
    Redis $redis,
    string $prefix,
    string $requestId,
    string $clientId,
    string $action,
    array $requestData,
    int $expireSeconds,
    int $recentLimit
): void {
    $key = redisKey($prefix, "request:{$requestId}");
    $clientRequestListKey = redisKey($prefix, "client:{$clientId}:requests");

    $item = [
        'requestId' => $requestId,
        'status' => 'pending',
        'clientId' => $clientId,
        'action' => $action,
        'request' => $requestData,
        'response' => null,
        'createdAt' => nowTime(),
        'responseAt' => 0,
        'timeoutSeconds' => getActionTimeoutSeconds($action, $requestData),
    ];

    $redis->set($key, json_encode($item, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES), $expireSeconds);

    $redis->lPush($clientRequestListKey, $requestId);
    $redis->lTrim($clientRequestListKey, 0, $recentLimit - 1);
    $redis->expire($clientRequestListKey, $expireSeconds);
}

function getRequestItem(Redis $redis, string $prefix, string $requestId): ?array
{
    $key = redisKey($prefix, "request:{$requestId}");
    $raw = $redis->get($key);
    if ($raw === false || $raw === null || $raw === '') {
        return null;
    }

    $data = json_decode($raw, true);
    return is_array($data) ? $data : null;
}

function saveRequestItem(Redis $redis, string $prefix, string $requestId, array $item, int $expireSeconds): void
{
    $key = redisKey($prefix, "request:{$requestId}");
    $redis->set($key, json_encode($item, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES), $expireSeconds);
}

function isScreenshotResponse(array $responseData): bool
{
    $action = trim((string)($responseData['action'] ?? ''));
    if ($action !== 'command') {
        return false;
    }

    $data = $responseData['data'] ?? [];
    if (!is_array($data)) {
        return false;
    }

    $command = trim((string)($data['command'] ?? ''));
    return $command === 'screen_screenshot' || $command === 'app_screenshot';
}

function guessExtensionByContentType(string $contentType): string
{
    $contentType = strtolower(trim($contentType));
    if ($contentType === 'image/png') return 'png';
    if ($contentType === 'image/webp') return 'webp';
    return 'jpg';
}

function saveScreenshotFileAndRewriteResponse(
    Redis $redis,
    string $prefix,
    array $responseData,
    string $requestId,
    string $screenshotDir,
    int $tokenExpire
): array {
    $data = $responseData['data'] ?? [];
    if (!is_array($data)) {
        return $responseData;
    }

    $imageBase64 = (string)($data['imageBase64'] ?? '');
    if ($imageBase64 === '') {
        return $responseData;
    }

    $binary = base64_decode($imageBase64, true);
    if ($binary === false || $binary === '') {
        return $responseData;
    }

    $contentType = trim((string)($data['contentType'] ?? 'image/jpeg'));
    $ext = guessExtensionByContentType($contentType);
    $safeRequestId = preg_replace('/[^a-zA-Z0-9_\-\.]/', '_', $requestId);
    $fileName = trim((string)($data['fileName'] ?? ''));
    if ($fileName === '') {
        $fileName = $safeRequestId . '.' . $ext;
    }

    $saveName = date('Ymd_His') . '_' . uniqid() . '_' . $safeRequestId . '.' . $ext;
    $fullPath = rtrim($screenshotDir, DIRECTORY_SEPARATOR) . DIRECTORY_SEPARATOR . $saveName;

    if (@file_put_contents($fullPath, $binary) === false) {
        return $responseData;
    }

    $fileToken = uniqid('file_', true);
    $fileMeta = [
        'token' => $fileToken,
        'requestId' => $requestId,
        'path' => $fullPath,
        'fileName' => $fileName,
        'contentType' => $contentType,
        'size' => filesize($fullPath) ?: 0,
        'createdAt' => nowTime(),
    ];

    $redis->set(
        redisKey($prefix, "file:{$fileToken}"),
        json_encode($fileMeta, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES),
        $tokenExpire
    );

    unset($data['imageBase64']);

    $data['fileToken'] = $fileToken;
    $data['fileUrl'] = '/file?token=' . urlencode($fileToken);
    $data['fileName'] = $fileName;
    $data['contentType'] = $contentType;
    $data['size'] = $fileMeta['size'];

    $responseData['data'] = $data;
    return $responseData;
}

function getFileMeta(Redis $redis, string $prefix, string $fileToken): ?array
{
    $raw = $redis->get(redisKey($prefix, "file:{$fileToken}"));
    if ($raw === false || $raw === null || $raw === '') {
        return null;
    }

    $data = json_decode($raw, true);
    return is_array($data) ? $data : null;
}

$server->on('Open', function (Server $server, Request $request) {
    echo "[" . date('Y-m-d H:i:s') . "] WebSocket connected, fd={$request->fd}\n";
});

$server->on('Message', function (Server $server, Frame $frame) use (
    $serverToken,
    $redisHost,
    $redisPort,
    $redisAuth,
    $redisPrefix,
    $clientInfoExpire,
    $requestExpire,
    $screenshotDir,
    $screenshotTokenExpire
) {
    $redis = getRedis($redisHost, $redisPort, $redisAuth);

    echo "[" . date('Y-m-d H:i:s') . "] message from fd={$frame->fd}: {$frame->data}\n";

    $data = json_decode($frame->data, true);
    if (!is_array($data)) {
        $server->push($frame->fd, json_encode([
            'type' => 'error',
            'message' => 'invalid_json'
        ], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
        return;
    }

    $type = trim((string)($data['type'] ?? ''));

    // 1) 客户端注册
    if ($type === 'register') {
        $clientId = trim((string)($data['clientId'] ?? ''));
        $token = trim((string)($data['token'] ?? ''));

        if ($clientId === '' || $token !== $serverToken) {
            $server->push($frame->fd, json_encode([
                'type' => 'register_ack',
                'success' => false,
                'message' => 'auth_failed'
            ], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
            return;
        }

        // 如已有旧连接，踢掉旧 fd
        $old = getClientInfo($redis, $redisPrefix, $clientId);
        if ($old && isset($old['fd'])) {
            $oldFd = (int)$old['fd'];
            if ($oldFd > 0 && $oldFd !== (int)$frame->fd && $server->isEstablished($oldFd)) {
                try {
                    $server->disconnect($oldFd, SWOOLE_WEBSOCKET_CLOSE_NORMAL, 'replaced_by_new_connection');
                } catch (Throwable $e) {
                }
            }
        }

        $extra = [
            'machineName' => trim((string)($data['machineName'] ?? '')),
            'version' => trim((string)($data['version'] ?? '')),
            'group' => trim((string)($data['group'] ?? '')),
            'localIp' => trim((string)($data['localIp'] ?? '')),
        ];

        saveClientInfo($redis, $redisPrefix, $clientId, (int)$frame->fd, $clientInfoExpire, $extra);

        $server->push($frame->fd, json_encode([
            'type' => 'register_ack',
            'success' => true,
            'clientId' => $clientId
        ], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));

        echo "[" . date('Y-m-d H:i:s') . "] Client registered: {$clientId} => fd {$frame->fd}\n";
        return;
    }

    // 2) 客户端回响应
    if ($type === 'response') {
        $requestId = trim((string)($data['requestId'] ?? ''));
        $clientId = trim((string)($data['clientId'] ?? ''));

        if ($requestId !== '') {
            $item = getRequestItem($redis, $redisPrefix, $requestId);
            if ($item) {
                $responseData = $data;

                // 截图响应自动落文件，避免大 base64 长期塞 Redis
                if (isScreenshotResponse($responseData)) {
                    $responseData = saveScreenshotFileAndRewriteResponse(
                        $redis,
                        $redisPrefix,
                        $responseData,
                        $requestId,
                        $screenshotDir,
                        $screenshotTokenExpire
                    );
                }

                $item['status'] = 'done';
                $item['response'] = $responseData;
                $item['responseAt'] = nowTime();

                $expireSeconds = getRequestExpireSeconds(
                    (string)($item['action'] ?? ''),
                    (array)($item['request'] ?? []),
                    $requestExpire
                );

                saveRequestItem($redis, $redisPrefix, $requestId, $item, $expireSeconds);

                echo "[" . date('Y-m-d H:i:s') . "] response saved: requestId={$requestId}, clientId={$clientId}\n";
            } else {
                echo "[" . date('Y-m-d H:i:s') . "] response ignored: requestId={$requestId} not found\n";
            }
        }
        return;
    }

    // 3) 客户端心跳
    if ($type === 'heartbeat') {
        $clientId = trim((string)($data['clientId'] ?? ''));
        if ($clientId !== '') {
            updateClientHeartbeat($redis, $redisPrefix, $clientId, $clientInfoExpire);
        }

        $server->push($frame->fd, json_encode([
            'type' => 'heartbeat_ack',
            'time' => nowTime()
        ], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
        return;
    }
});

$server->on('Close', function (Server $server, int $fd) use (
    $redisHost,
    $redisPort,
    $redisAuth,
    $redisPrefix
) {
    $redis = getRedis($redisHost, $redisPort, $redisAuth);
    $clientId = removeClientByFd($redis, $redisPrefix, $fd);

    if ($clientId !== null) {
        echo "[" . date('Y-m-d H:i:s') . "] Client close cleanup: {$clientId}, fd={$fd}\n";
    } else {
        echo "[" . date('Y-m-d H:i:s') . "] WebSocket closed, fd={$fd}\n";
    }
});

// HTTP 接口
$server->on('Request', function (Request $request, Response $response) use (
    $server,
    $redisHost,
    $redisPort,
    $redisAuth,
    $redisPrefix,
    $requestExpire,
    $clientRecentRequestLimit
) {
    $redis = getRedis($redisHost, $redisPort, $redisAuth);

    $allowOrigins = [
        'http://117.21.200.221',
        'http://127.0.0.1',
        'http://localhost',
        'http://117.21.200.221:8099',
        'http://127.0.0.1:8099',
        'http://localhost:8099',
    ];

    $origin = $request->header['origin'] ?? '';

    if (in_array($origin, $allowOrigins, true)) {
        $response->header('Access-Control-Allow-Origin', $origin);
        $response->header('Access-Control-Allow-Credentials', 'true');
    }

    $response->header('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    $response->header('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Requested-With');
    $response->header('Access-Control-Max-Age', '86400');
    $response->header('Vary', 'Origin');

    if (($request->server['request_method'] ?? '') === 'OPTIONS') {
        $response->status(204);
        $response->end();
        return;
    }

    $path = $request->server['request_uri'] ?? '/';

    // 在线客户端
    if ($path === '/online') {
        $onlineSetKey = redisKey($redisPrefix, 'clients:online');
        $clientIds = $redis->sMembers($onlineSetKey);
        $clients = [];
        $now = nowTime();

        foreach ($clientIds as $clientId) {
            $info = getClientInfo($redis, $redisPrefix, (string)$clientId);
            if (!$info) {
                $redis->sRem($onlineSetKey, $clientId);
                continue;
            }

            $lastHeartbeat = (int)($info['lastHeartbeat'] ?? 0);
            $online = $lastHeartbeat > 0 && (($now - $lastHeartbeat) <= 60);

            $clients[] = [
                'clientId' => $info['clientId'] ?? $clientId,
                'fd' => (int)($info['fd'] ?? 0),
                'machineName' => $info['machineName'] ?? '',
                'version' => $info['version'] ?? '',
                'group' => $info['group'] ?? '',
                'localIp' => $info['localIp'] ?? '',
                'lastHeartbeat' => $lastHeartbeat,
                'connectedAt' => (int)($info['connectedAt'] ?? 0),
                'updatedAt' => (int)($info['updatedAt'] ?? 0),
                'online' => $online,
                'stateText' => $online ? 'online' : 'offline',
            ];
        }

        jsonResponse($response, [
            'success' => true,
            'prefix' => $redisPrefix,
            'count' => count($clients),
            'clients' => $clients
        ]);
        return;
    }

    // 下发消息
    if ($path === '/send' && ($request->server['request_method'] ?? '') === 'POST') {
        $body = readJsonBody($request);

        $clientId = trim((string)($body['clientId'] ?? ''));
        $action = trim((string)($body['action'] ?? ''));
        $payload = $body['payload'] ?? [];

        if ($clientId === '' || $action === '') {
            jsonResponse($response, [
                'success' => false,
                'message' => 'clientId/action required'
            ], 400);
            return;
        }

        $clientInfo = getClientInfo($redis, $redisPrefix, $clientId);
        if (!$clientInfo) {
            jsonResponse($response, [
                'success' => false,
                'message' => 'client offline'
            ], 404);
            return;
        }

        $fd = (int)($clientInfo['fd'] ?? 0);
        if ($fd <= 0 || !$server->isEstablished($fd)) {
            removeClientById($redis, $redisPrefix, $clientId);

            jsonResponse($response, [
                'success' => false,
                'message' => 'client websocket not established'
            ], 410);
            return;
        }

        $requestId = uniqid('req_', true);

        $message = [
            'type' => 'request',
            'requestId' => $requestId,
            'action' => $action,
            'payload' => $payload,
            'time' => nowTime()
        ];

        $expireSeconds = getRequestExpireSeconds($action, $message, $requestExpire);

        saveRequest(
            $redis,
            $redisPrefix,
            $requestId,
            $clientId,
            $action,
            $message,
            $expireSeconds,
            $clientRecentRequestLimit
        );

        $ok = $server->push($fd, json_encode($message, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
        if (!$ok) {
            $item = getRequestItem($redis, $redisPrefix, $requestId);
            if ($item) {
                $item['status'] = 'timeout';
                $item['responseAt'] = nowTime();
                $item['response'] = [
                    'type' => 'system_error',
                    'message' => 'push failed'
                ];
                saveRequestItem($redis, $redisPrefix, $requestId, $item, $expireSeconds);
            }

            jsonResponse($response, [
                'success' => false,
                'message' => 'push failed'
            ], 500);
            return;
        }

        echo "[" . date('Y-m-d H:i:s') . "] send to clientId={$clientId}, fd={$fd}, requestId={$requestId}, action={$action}\n";

        jsonResponse($response, [
            'success' => true,
            'requestId' => $requestId,
            'message' => 'sent'
        ]);
        return;
    }

    // 查询某次请求结果
    if ($path === '/result' && ($request->server['request_method'] ?? '') === 'GET') {
        $requestId = trim((string)($request->get['requestId'] ?? ''));

        if ($requestId === '') {
            jsonResponse($response, [
                'success' => false,
                'message' => 'requestId required'
            ], 400);
            return;
        }

        $item = getRequestItem($redis, $redisPrefix, $requestId);
        if (!$item) {
            jsonResponse($response, [
                'success' => false,
                'message' => 'requestId not found'
            ], 404);
            return;
        }

        $timeoutSeconds = (int)($item['timeoutSeconds'] ?? 30);
        if (($item['status'] ?? '') === 'pending' && (nowTime() - (int)($item['createdAt'] ?? 0)) > $timeoutSeconds) {
            $item['status'] = 'timeout';

            $expireSeconds = getRequestExpireSeconds(
                (string)($item['action'] ?? ''),
                (array)($item['request'] ?? []),
                $requestExpire
            );
            saveRequestItem($redis, $redisPrefix, $requestId, $item, $expireSeconds);
        }

        jsonResponse($response, [
            'success' => true,
            'requestId' => $requestId,
            'status' => $item['status'] ?? 'unknown',
            'clientId' => $item['clientId'] ?? '',
            'action' => $item['action'] ?? '',
            'timeoutSeconds' => $timeoutSeconds,
            'createdAt' => (int)($item['createdAt'] ?? 0),
            'responseAt' => (int)($item['responseAt'] ?? 0),
            'response' => $item['response'] ?? null
        ]);
        return;
    }

    // 查询某个客户端最近请求
    if ($path === '/client-requests' && ($request->server['request_method'] ?? '') === 'GET') {
        $clientId = trim((string)($request->get['clientId'] ?? ''));
        $limit = (int)($request->get['limit'] ?? 20);
        $limit = max(1, min(100, $limit));

        if ($clientId === '') {
            jsonResponse($response, [
                'success' => false,
                'message' => 'clientId required'
            ], 400);
            return;
        }

        $clientRequestListKey = redisKey($redisPrefix, "client:{$clientId}:requests");
        $requestIds = $redis->lRange($clientRequestListKey, 0, $limit - 1);

        $items = [];
        foreach ($requestIds as $requestId) {
            $item = getRequestItem($redis, $redisPrefix, (string)$requestId);
            if ($item) {
                $items[] = [
                    'requestId' => $item['requestId'] ?? $requestId,
                    'status' => $item['status'] ?? '',
                    'action' => $item['action'] ?? '',
                    'timeoutSeconds' => (int)($item['timeoutSeconds'] ?? 30),
                    'createdAt' => (int)($item['createdAt'] ?? 0),
                    'responseAt' => (int)($item['responseAt'] ?? 0),
                    'response' => $item['response'] ?? null,
                ];
            }
        }

        jsonResponse($response, [
            'success' => true,
            'clientId' => $clientId,
            'items' => $items
        ]);
        return;
    }

    // 图片文件访问
    if ($path === '/file' && ($request->server['request_method'] ?? '') === 'GET') {
        $token = trim((string)($request->get['token'] ?? ''));
        if ($token === '') {
            jsonResponse($response, [
                'success' => false,
                'message' => 'token required'
            ], 400);
            return;
        }

        $meta = getFileMeta($redis, $redisPrefix, $token);
        if (!$meta) {
            jsonResponse($response, [
                'success' => false,
                'message' => 'file token not found'
            ], 404);
            return;
        }

        $pathFile = (string)($meta['path'] ?? '');
        if ($pathFile === '' || !is_file($pathFile)) {
            jsonResponse($response, [
                'success' => false,
                'message' => 'file not found'
            ], 404);
            return;
        }

        $contentType = (string)($meta['contentType'] ?? 'application/octet-stream');
        $fileName = (string)($meta['fileName'] ?? basename($pathFile));

        $response->header('Content-Type', $contentType);
        $response->header('Content-Disposition', 'inline; filename="' . rawurlencode($fileName) . '"');
        $response->sendfile($pathFile);
        return;
    }

    // 清理无效在线集合成员
    if ($path === '/cleanup-online' && ($request->server['request_method'] ?? '') === 'POST') {
        $onlineSetKey = redisKey($redisPrefix, 'clients:online');
        $clientIds = $redis->sMembers($onlineSetKey);
        $deleted = 0;

        foreach ($clientIds as $clientId) {
            $info = getClientInfo($redis, $redisPrefix, (string)$clientId);
            if (!$info) {
                $redis->sRem($onlineSetKey, $clientId);
                $deleted++;
            }
        }

        jsonResponse($response, [
            'success' => true,
            'deleted' => $deleted
        ]);
        return;
    }

    jsonResponse($response, [
        'success' => false,
        'message' => 'not found'
    ], 404);
});

$server->start();