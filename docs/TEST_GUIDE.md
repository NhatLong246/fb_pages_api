# Test Guide - CoreService Pipeline

## 1) Muc tieu

Tai lieu nay huong dan test day du cac testcase cho pipeline:

- Spam detection
- AI intent/sentiment
- Decision engine
- Action execution
- Retry va failed flow
- Auto reply theo sentiment
- Test comment that tren Facebook Page

## 2) Pham vi

- WebhookService nhan event Facebook va publish Kafka topic `raw_events`
- CoreService consume `raw_events`, xu ly pipeline, ghi DB va publish `reply_commands`
- BackendApi consume `reply_commands`, `send_retry`, kiem tra idempotency va goi Facebook Graph API
- RetryService consume topic `send_failed`, republish `send_retry`, qua nguong thi publish `dead_letter`

## 3) Chuan bi moi truong

### 3.1 Config bat buoc

Kiem tra cac file:

- `WebhookService/appsettings.Development.json`
- `CoreService/appsettings.Development.json`
- `BackendApi/appsettings.Development.json`

Can co:

- WebhookService Facebook `PageId`, `AppSecret`, `VerifyToken`
- BackendApi Facebook `PageId`, `PageAccessToken`
- BackendApi `Dashboard:AdminApiKey` neu muon bat buoc dashboard gui header `X-Admin-Key`
- Gemini `ApiKey`
- Kafka `Topic = raw_events`, `FailedTopic = send_failed`, `RetryTopic = send_retry`, `DeadLetterTopic = dead_letter`

Tao file `.env` tu `.env.example` neu muon nhan Slack alert:

```powershell
Copy-Item .env.example .env
```

Sau do thay `YOUR/SLACK/WEBHOOK` bang Incoming Webhook URL that cua Slack.

### 3.2 Start ha tang

```powershell
docker compose up -d
```

### 3.3 Migrate DB

```powershell
dotnet ef database update --project CoreService\CoreService.csproj
```

### 3.4 Chay services

```powershell
dotnet run --project WebhookService\WebhookService.csproj --launch-profile webhook-service
dotnet run --project CoreService\CoreService.csproj --launch-profile core-service
dotnet run --project BackendApi\BackendApi.csproj --launch-profile backend-api
dotnet run --project RetryService\RetryService.csproj --launch-profile retry-service
```

Health check:

```powershell
Invoke-WebRequest -Uri "http://localhost:3001/health" -UseBasicParsing
Invoke-WebRequest -Uri "http://localhost:3002/swagger" -UseBasicParsing
Invoke-WebRequest -Uri "http://localhost:3003/health" -UseBasicParsing
```

## 4) Test local bang script webhook

Script:

- `tests/demo.ps1`

Vi du:

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\demo.ps1 -FromId "user-a" -FromName "User A" -Message "Shop oi gia bao nhieu?"
```

Luu y:

- Script tu tao `comment_id` moi, khong bi duplicate event id.
- Neu `comment_id` la gia (khong ton tai tren Facebook), cac action hide/block goi Graph API co the fail `400`, day la expected cho test local payload gia.

## 5) Checklist testcase

### TC01 - Intent inquiry + neutral

- Input: `Shop oi gia bao nhieu?`
- Expected:
  - `IsSpam = false`
  - `Intent = inquiry`
  - `Sentiment = neutral`
  - `ActionTaken = None`
  - `Status = Processed`

### TC02 - Intent complaint + negative

- Input: `Minh chua nhan duoc hang`
- Expected:
  - `Intent = complaint`
  - `Sentiment = negative`
  - `ActionTaken = ReplyNegative`
  - BackendApi consume command va cap nhat `Status = Replied`
  - Facebook co reply xin loi va de nghi ho tro

### TC03 - Intent praise + positive

- Input: `Bai viet hay qua`
- Expected:
  - `Intent = praise`
  - `Sentiment = positive`
  - `ActionTaken = ReplyPositive`
  - BackendApi consume command va cap nhat `Status = Replied`
  - Facebook co reply cam on

### TC04 - Light spam (link)

- Input: `Xem ngay https://abc.com khuyen mai`
- Expected:
  - `IsSpam = true`
  - `SpamSeverity = Light`
  - `ActionTaken = HideComment`
  - Neu hide fail do comment gia: `Status = Failed`, `RetryCount` tang den max, event vao `send_failed`

### TC05 - Repeated spam 3 lan / 24h -> blacklist

- Gui 3 comment link voi cung `FromId` trong 24h.
- Expected:
  - Lan dat nguong: `ActionTaken = BlacklistUser`
  - Co record trong `BlacklistedUsers`

### TC06 - Harmful/scam -> review queue

- Input: `free money investment guaranteed click here`
- Expected:
  - `IsSpam = true`, `SpamSeverity = Harmful`
  - `ActionTaken = QueueForReview`
  - Co record trong `ReviewQueueItems` voi `Reason = harmful_or_scam`

### TC07 - Retry flow send_failed

- Cach test de quan sat retry:
  - Dung payload local co `comment_id` gia (mac dinh script)
  - Trigger action can goi Facebook API (Hide/Block)
- Expected:
  - Event fail duoc publish `send_failed`
  - RetryService republish lai `send_retry` sau 1s, 2s, 4s
  - `RetryCount` dung tai `MaxRetryAttempts` (hien tai = 3)
  - Event qua nguong duoc publish `dead_letter`

### TC08 - Tai pham nhieu lan -> block flow + manual queue

- Gui nhieu spam cung user de vuot nguong block.
- Expected:
  - `ActionTaken = BlockUser`
  - Event duoc enqueue review voi `Reason = manual_block_required`
  - Neu API block fail thi pipeline van khong crash; event co the processed neu khong throw ra ngoai flow

## 6) Query nhanh de doi chieu DB

Truy van bang `psql` trong postgres container:

```powershell
docker exec -it pageapi123-postgres-1 psql -U postgres -d coreservice_db
```

Trong psql:

```sql
select "EventId","ActorId","Status","RetryCount","IsSpam","SpamSeverity","Intent","Sentiment","ActionTaken","DecisionReason","UpdatedAt"
from "EventStates"
order by "UpdatedAt" desc
limit 50;

select * from "BlacklistedUsers" order by "LastSpamAt" desc limit 20;

select * from "ReviewQueueItems" order by "QueuedAt" desc limit 50;

select "CommandId","EventId","Status","RetryCount","ErrorMessage","UpdatedAt"
from "CommandExecutions"
order by "UpdatedAt" desc
limit 50;
```

Map nhanh enum:

- `Status`: `2 = Processed`, `3 = Replied`, `4 = Failed`
- `ActionTaken`: `0=None`, `1=HideComment`, `2=BlacklistUser`, `3=QueueForReview`, `4=BlockUser`, `5=ReplyPositive`, `6=ReplyNegative`

## 7) Test comment that tren Facebook Page

### 7.1 Publish webhook endpoint bang ngrok

```powershell
ngrok http 3001
```

Lay URL:

- `https://<id>.ngrok-free.app/webhook`

### 7.2 Cau hinh Meta Webhooks

Trong Meta Developer:

1. Chon App -> Webhooks -> Object `Page`
2. Callback URL: URL ngrok + `/webhook`
3. Verify token: trung voi `Facebook:VerifyToken` cua WebhookService
4. Subscribe field: `feed`
5. Subscribe app vao dung Facebook Page can test

### 7.3 Test truong hop comment that

Tao 1 bai post tren Page, dung tai khoan user that comment:

1. `Shop oi gia bao nhieu?`
2. `Minh chua nhan duoc hang`
3. `Bai viet hay qua`
4. `Xem ngay https://abc.com khuyen mai`
5. `free money investment guaranteed click here`

Expected khi test that:

- Event vao DB voi `EventId` theo comment that
- Case spam/harmful co hanh vi hide that tren page (neu token/quyen dung)
- Case harmful co record trong `ReviewQueueItems`
- Neu can test block that: can chuan bi account test va quyen Page phu hop, sau do verify tren Facebook UI (hoac endpoint blocked users neu ban da co luong kiem chung)

## 8) Tieu chi PASS

PASS khi:

1. Webhook -> Kafka -> CoreService xu ly duoc event, khong mat event.
2. State tracking trong `EventStates` dung voi tung testcase.
3. Retry/failed flow hoat dong, retry dung nguong.
4. Blacklist/review queue duoc ghi dung theo rule.
5. Test comment that tren page cho ra hanh vi moderation dung voi expectation.
6. BackendApi log `[IDEMPOTENCY] Duplicate skipped` khi command trung lap.
7. Message het retry vao `dead_letter`; Prometheus co alert `DeadLetterQueueReceived`.

## 9) Monitoring

- Kafka UI: `http://localhost:8080`
- Kafka exporter metrics: `http://localhost:9308/metrics`
- Prometheus targets: `http://localhost:9090/targets`
- Prometheus alerts: `http://localhost:9090/alerts`
- Alertmanager: `http://localhost:9093`

Khong commit file `.env` vi file nay chua Slack webhook secret.

## 10) Su co thuong gap

- `Facebook API 400 unsupported post request`:
  - Thuong do `comment_id` gia khi test local payload.
- Event khong vao CoreService:
  - Kiem tra topic config co dong bo `raw_events`.
  - Kiem tra CoreService dang chay profile `http` va ket noi Kafka.
- Intent/sentiment ra `other/neutral`:
  - Kiem tra Gemini key.
  - Hien co fallback rule-based cho 3 nhom mau chinh.
