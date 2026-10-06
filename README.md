# Fall26 SEP490 G54

Dự án full-stack sử dụng ASP.NET Core 8, Angular 22, PostgreSQL 16, Redis 7 và Docker Compose. Dự án đã có sẵn DbMigrator, health check, Swagger, JWT skeleton, Serilog, xử lý lỗi tập trung, runtime environment cho frontend, unit test, CI và bộ code review có phân loại security theo mức độ.

## Mục Lục

- [Tổng quan](#tổng-quan)
- [Công nghệ](#công-nghệ)
- [Kiến trúc](#kiến-trúc)
- [Yêu cầu môi trường](#yêu-cầu-môi-trường)
- [Chạy project](#chạy-project)
- [Authentication](#authentication)
- [Địa chỉ dịch vụ](#địa-chỉ-dịch-vụ)
- [Cấu hình development](#cấu-hình-development)
- [Database và DbMigrator](#database-và-dbmigrator)
- [Frontend environment](#frontend-environment)
- [Code review và security](#code-review-và-security)
- [Docker và deployment](#docker-và-deployment)
- [CI](#ci)
- [Agent skills](#agent-skills)
- [Lệnh thường dùng](#lệnh-thường-dùng)
- [Troubleshooting](#troubleshooting)
- [Trạng thái xác minh](#trạng-thái-xác-minh)

## Tổng Quan

Dự án gồm các thành phần:

- REST API viết bằng ASP.NET Core 8 MVC Controllers.
- Business layer và data access layer tách riêng theo kiến trúc N-layer.
- PostgreSQL lưu trữ dữ liệu chính.
- Redis dùng cho distributed cache và health readiness.
- DbMigrator là console app chuyên dụng để apply EF Core migrations và seed data.
- Angular standalone application dùng strict TypeScript.
- nginx serve frontend production và proxy `/api` sang backend.
- Docker Compose khởi động PostgreSQL, Redis, pgAdmin, DbMigrator, API và frontend theo đúng thứ tự.
- Code review scripts kiểm tra build, test, format, dependency vulnerabilities và các mẫu code không an toàn.

Trang mẫu hiện tại gọi `GET /api/hello` và hiển thị `Hello World`, dùng để xác minh kết nối frontend-backend.

## Công Nghệ

| Thành phần | Công nghệ |
| --- | --- |
| Backend | .NET 8, ASP.NET Core MVC Controllers |
| Business layer | C# services và dependency injection |
| Data layer | EF Core 8, Npgsql |
| Database | PostgreSQL 16 |
| Cache | Redis 7 |
| Migration | EF Core migrations, `G54.DbMigrator` |
| API docs | Swagger / OpenAPI |
| Authentication skeleton | JWT Bearer |
| Logging | Serilog |
| Frontend | Angular 22, TypeScript 6, RxJS |
| Frontend server | nginx |
| Test | xUnit, Jasmine/Karma |
| Tooling | ESLint, Prettier, Husky, lint-staged |
| Container | Docker Desktop, Docker Compose, WSL2 trên Windows |
| CI | GitHub Actions |

Phiên bản .NET SDK được ghim trong `global.json`:

```text
8.0.425
```

Frontend yêu cầu Node.js `>=22 <23` và npm `>=10 <11`.

## Kiến Trúc

```text
Fall26_SEP490_G54/
|-- backend/
|   |-- G54.sln
|   |-- Directory.Build.props
|   |-- Dockerfile.api
|   |-- Dockerfile.migrator
|   |-- src/
|   |   |-- G54.Api/          Controllers, middleware, health checks, startup
|   |   |-- G54.BLL/          Services, interfaces, DTOs, business rules
|   |   |-- G54.DAL/          DbContext, entities, migrations, seed data
|   |   `-- G54.DbMigrator/   Apply migrations và seed database
|   `-- tests/
|       `-- G54.UnitTests/    xUnit tests
|-- frontend/
|   |-- src/app/core/         API services và core infrastructure
|   |-- src/environments/     Development và production environment
|   |-- scripts/replace_env.sh
|   |-- nginx.conf
|   `-- Dockerfile
|-- docker/
|   |-- docker-compose.yml
|   |-- docker-compose.prod.yml
|   `-- .env.example
|-- docs/codereview/          Backend/frontend standards và reports
|-- scripts/                  Code review scripts
|-- .opencode/skills/         Skills cho OpenCode agents
|-- .claude/skills/           Skills tương thích Claude Code
|-- AGENTS.md                 Quy tắc chung cho agents
|-- run.ps1                   Development runner cho Windows
`-- run.sh                    Development runner cho Unix
```

Hướng phụ thuộc (Dependency direction) của backend:

```text
G54.Api -> G54.BLL -> G54.DAL
G54.DbMigrator ------> G54.DAL
```

Quy tắc bắt buộc:

- Controller chỉ xử lý HTTP và gọi BLL service.
- Controller không được truy cập `AppDbContext` hoặc DAL trực tiếp.
- Business rules và authorization theo resource nằm trong BLL.
- Entity, EF Core và database logic nằm trong DAL.
- API không tự động migrate database khi khởi động; DbMigrator chịu trách nhiệm này.

## Yêu Cầu Môi Trường

### Windows

- Windows 10/11 64-bit.
- .NET SDK 8.0.425.
- Node.js 22 LTS và npm 10.
- Docker Desktop với WSL2 nếu chạy infrastructure hoặc full stack bằng Docker.
- PowerShell 5.1 trở lên.

Kiểm tra:

```powershell
dotnet --version
node --version
npm --version
docker version
docker compose version
wsl --version
```

Kết quả mong đợi:

```text
.NET SDK: 8.0.425
Node.js:   22.x
npm:       10.x
```

### Linux/macOS

- .NET SDK 8.
- Node.js 22 LTS.
- Docker Engine/Desktop và Docker Compose.
- Shell tương thích `sh`.

## Chạy Project

### Cách 1: Development với hot reload

Đây là cách nên dùng khi lập trình. PostgreSQL, Redis và pgAdmin chạy bằng Docker; API và Angular chạy native với hot reload.

Windows:

```powershell
./run.ps1
```

Linux/macOS:

```sh
./run.sh
```

Runner sẽ:

1. Khởi động PostgreSQL, Redis và pgAdmin nếu Docker khả dụng.
2. Chạy `G54.DbMigrator` để apply migration và seed data.
3. Khởi động API bằng `dotnet watch` tại port `5000`.
4. Khởi động Angular dev server tại port `4200`.

Trên Windows, API và frontend được mở trong hai cửa sổ PowerShell riêng.

## Authentication

- Sign-in is available at `/login`; successful login opens `/home`. Logout revokes the server session and clears the client session.
- `POST /api/v1/auth/login` accepts `email`, `password`, and `rememberMe`; roles and permissions are returned in the JWT.
- `POST /api/v1/auth/logout` requires an access token and a `refreshToken`; the API revokes the refresh session and blacklists the access token in Redis.
- Failed logins are audited; five consecutive failures lock the account for 15 minutes.
- Access tokens expire after 15 minutes; rotating refresh tokens expire after 12 hours, or up to 30 days with `rememberMe`.
- Refresh tokens are kept in browser storage; prevent XSS and do not render untrusted HTML.

### Cách 2: Chạy toàn bộ bằng Docker

Windows:

```powershell
./run.ps1 -Docker
```

Hoặc chạy trực tiếp:

```powershell
docker compose --env-file docker/.env -f docker/docker-compose.yml up --build
```

Chạy nền (background):

```powershell
docker compose --env-file docker/.env -f docker/docker-compose.yml up --build -d
```

Thứ tự khởi động được Compose đảm bảo:

```text
PostgreSQL healthy
  -> DbMigrator exit 0
  -> Redis healthy
  -> API healthy
  -> Frontend start
```

### Dừng project

```powershell
./run.ps1 -Stop
```

Hoặc:

```powershell
docker compose --env-file docker/.env -f docker/docker-compose.yml down
```

Lệnh trên giữ lại các volume của PostgreSQL và Redis.

Xóa container và toàn bộ dữ liệu development:

```powershell
docker compose --env-file docker/.env -f docker/docker-compose.yml down -v
```

Cẩn thận: `down -v` xóa database và cache hiện tại.

## Địa Chỉ Dịch Vụ

| Dịch vụ | Native development | Full Docker |
| --- | --- | --- |
| Frontend | http://localhost:4200 | http://localhost:8080 |
| API Hello | http://localhost:5000/api/hello | http://localhost:5000/api/hello |
| Swagger | http://localhost:5000/swagger | http://localhost:5000/swagger |
| Liveness | http://localhost:5000/health | http://localhost:5000/health |
| Readiness | http://localhost:5000/health/ready | http://localhost:5000/health/ready |
| pgAdmin | http://localhost:5050 | http://localhost:5050 |
| PostgreSQL | localhost:5432 | localhost:5432 |
| Redis | localhost:6379 | localhost:6379 |

Swagger chỉ được bật khi API chạy với `ASPNETCORE_ENVIRONMENT=Development`.

## Cấu Hình Development

Tạo file local environment nếu chưa có:

```powershell
Copy-Item docker/.env.example docker/.env
```

Thông tin đăng nhập (credential) development mặc định:

```text
PostgreSQL database: postgres
PostgreSQL username: postgres
PostgreSQL password: postgres

pgAdmin email:       admin@example.com
pgAdmin password:    admin
```

Kết nối PostgreSQL từ pgAdmin khi pgAdmin cũng chạy trong Compose:

```text
Host: postgres
Port: 5432
Database: postgres
Username: postgres
Password: postgres
```

Kết nối từ công cụ native trên máy, ví dụ DBeaver:

```text
Host: localhost
Port: 5432
Database: postgres
Username: postgres
Password: postgres
```

Đây chỉ là credential development. Không sử dụng `postgres/postgres` hoặc `admin/admin` khi deploy production.

### Cấu hình backend

Backend đọc cấu hình từ `appsettings.json`, `appsettings.Development.json` và các biến môi trường (environment variables). Khóa lồng nhau (nested key) sử dụng dấu gạch dưới kép `__`:

```text
ConnectionStrings__Postgres
ConnectionStrings__Redis
Jwt__Key
ASPNETCORE_ENVIRONMENT
```

Ví dụ:

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres"
$env:ConnectionStrings__Redis = "localhost:6379,abortConnect=false"
$env:Jwt__Key = "replace-with-a-random-secret-at-least-32-characters"
```

## Database Và DbMigrator

### Luồng migration

`G54.DbMigrator`:

1. Đọc `ConnectionStrings__Postgres`.
2. Tạo `AppDbContext`.
3. Gọi `Database.MigrateAsync()`.
4. Gọi `DatabaseSeeder.SeedAsync()`.
5. Trả về exit code `0` nếu thành công.

Trong Docker, API chỉ khởi động sau khi DbMigrator thoát với exit code `0`.

### Chạy DbMigrator native

Đảm bảo PostgreSQL đang chạy, sau đó:

```powershell
dotnet run --project backend/src/G54.DbMigrator/G54.DbMigrator.csproj
```

### Tạo migration mới

Cài công cụ EF một lần:

```powershell
dotnet tool install --global dotnet-ef --version 8.*
```

Tạo migration:

```powershell
dotnet ef migrations add <MigrationName> `
  --project backend/src/G54.DAL `
  --startup-project backend/src/G54.DbMigrator
```

Sau khi tạo migration:

```powershell
dotnet run --project backend/src/G54.DbMigrator/G54.DbMigrator.csproj
./scripts/codereview-backend.ps1
```

Quy tắc migration:

- Mỗi thay đổi schema phải có EF Core migration.
- Seed data phải idempotent, chạy lại không tạo bản ghi trùng lặp (duplicate).
- Không sửa migration đã được deploy.
- Thay đổi có tính phá hủy (destructive) như drop column/table cần có kế hoạch migration dữ liệu (data migration plan).
- Không gọi `Database.Migrate()` trong quá trình khởi động API (API startup).

## Frontend Environment

Frontend có hai file:

```text
frontend/src/environments/environment.development.ts
frontend/src/environments/environment.ts
```

Development build thay `environment.ts` bằng `environment.development.ts` qua `fileReplacements` trong `angular.json`.

File production không chứa deployment URL thật mà chứa placeholder:

```typescript
export const environment = {
  production: true,
  apiUrl: '__API_URL__',
  appVersion: '__APP_VERSION__',
};
```

Khi nginx container khởi động, `frontend/scripts/replace_env.sh` thay placeholder bằng:

```text
API_URL
APP_VERSION
```

Nhờ vậy một frontend image có thể deploy lên nhiều môi trường mà không cần build lại.

Khi thêm biến frontend mới:

1. Thêm giá trị development vào `environment.development.ts`.
2. Thêm placeholder vào `environment.ts`.
3. Thêm logic replace vào `scripts/replace_env.sh`.
4. Thêm biến vào `docker/.env.example` và Compose.
5. Chạy frontend code review.

nginx production đã được cấu hình sẵn:

- SPA fallback về `index.html`.
- Proxy `/api/` sang API container.
- Content Security Policy (CSP).
- `X-Content-Type-Options: nosniff`.
- `X-Frame-Options: DENY`.
- `Referrer-Policy` và `Permissions-Policy`.
- Chặn các file public source-map.
- Tắt nginx version token.

## Code Review Và Security

### Chạy review

Backend:

```powershell
./scripts/codereview-backend.ps1
```

Frontend:

```powershell
./scripts/codereview-frontend.ps1
```

Unix wrappers:

```sh
./scripts/codereview-backend.sh
./scripts/codereview-frontend.sh
```

Tiêu chuẩn chi tiết:

```text
docs/codereview/CODEREVIEW_BACKEND.md
docs/codereview/CODEREVIEW_FRONTEND.md
```

Báo cáo được sinh tại:

```text
docs/codereview/reports/BACKEND_REPORT.md
docs/codereview/reports/FRONTEND_REPORT.md
```

Không sửa reports bằng tay. Reports phải được tạo tự động bởi scripts.

### Nội dung tự động kiểm tra

Backend review kiểm tra:

- NuGet restore.
- Release build với cấu hình cảnh báo được coi là lỗi (warnings treated as errors).
- xUnit tests.
- `dotnet format`.
- Controller không truy cập DAL/AppDbContext.
- Hardcoded private keys/cloud keys/production credentials.
- Raw SQL interpolation và concatenation.
- JWT validation flags bị tắt.
- Insecure deserialization.
- High/Critical vulnerable NuGet dependencies.
- CORS `AllowAnyOrigin` kết hợp với credentials.

Frontend review kiểm tra:

- ESLint và Prettier.
- Headless unit tests.
- Production build.
- Hardcoded API URLs.
- `console.log` trong application code.
- Secrets/private keys trong frontend source.
- `bypassSecurityTrust*` không được review.
- Direct DOM injection và dynamic code execution.
- High/Critical runtime npm vulnerabilities.
- Production API URL dùng plain HTTP.

### Mức độ nghiêm trọng của bảo mật (Security severity)

| Severity | Ý nghĩa | Policy |
| --- | --- | --- |
| Critical | Có thể trực tiếp gây data breach, auth bypass, RCE, XSS hoặc lộ lọt bí mật | Chặn merge (Block merge), sửa ngay |
| High | Lỗ hổng nghiêm trọng, có thể khai thác với nỗ lực vừa phải | Chặn merge (Block merge) |
| Medium | Lỗ hổng thuộc lớp phòng thủ theo chiều sâu (defense-in-depth gap), cần thêm điều kiện để khai thác | Tạo issue và sửa trong sprint |
| Low | Tăng cường bảo mật (hardening) hoặc sai lệch so với best practice | Đưa vào backlog |
| Info | Gợi ý hoặc quan sát ghi nhận | Không bắt buộc |

Review chỉ pass khi:

- Tất cả automated gates đều pass.
- Không có Critical findings.
- Không có High findings.
- Medium findings có follow-up issue rõ ràng.

Định dạng finding:

```text
[CRITICAL] SEC-BE-C04 Resource ownership is not checked
File: backend/src/G54.BLL/Services/ExampleService.cs:42
Risk: Authenticated users can access another user's resource.
Fix: Filter by current user id and return 404 when not owned.
```

## Docker Và Deployment

### Development Compose

`docker/docker-compose.yml` mở (expose) các port để debug local và có tích hợp pgAdmin.

Phụ thuộc trạng thái hoạt động (Health dependencies):

- PostgreSQL: `pg_isready`.
- Redis: `redis-cli ping`.
- API: `curl http://localhost:8080/health`.
- DbMigrator: phải thoát với exit code `0`.

### Production overlay

```powershell
docker compose `
  --env-file docker/.env `
  -f docker/docker-compose.yml `
  -f docker/docker-compose.prod.yml `
  up --build -d
```

Production overlay:

- Không mở (expose) port PostgreSQL ra bên ngoài.
- Không mở (expose) port Redis ra bên ngoài.
- Tắt pgAdmin.
- Đặt môi trường API thành `Production`.

Trước khi deploy production:

1. Đổi username/password của PostgreSQL.
2. Tạo `JWT_KEY` ngẫu nhiên, tối thiểu 256 bit.
3. Không commit file `docker/.env`.
4. Đặt reverse proxy/TLS phía trước frontend/API.
5. Không mở database và Redis ra Internet.
6. Dùng user database cho production theo nguyên tắc đặc quyền tối thiểu (least privilege), không dùng superuser `postgres`.
7. Kiểm tra `docker compose config` và chạy cả hai script code review.

## CI

GitHub Actions workflows:

```text
.github/workflows/backend-ci.yml
.github/workflows/frontend-ci.yml
.github/workflows/codeql.yml
```

Backend và frontend workflows chạy theo bộ lọc `paths`, thực thi script code review và upload generated report làm artifact.

## Agent Skills

OpenCode skills nằm trong `.opencode/skills/`:

| Skill | Khi nào dùng |
| --- | --- |
| `backend-feature` | Controller, BLL service, entity, DTO, backend test |
| `db-migration` | Schema, migration, seed data, DbMigrator |
| `frontend-feature` | Angular component, route, service, environment |
| `code-review` | Chạy quality gates và đọc reports |
| `security-review` | Review Critical/High/Medium/Low security findings |
| `deploy` | Docker, run scripts, nginx và deployment |

Các skill cũng được mirror tại `.claude/skills/`.

Sau khi sửa hoặc thêm skill, hãy khởi động lại (restart) phiên OpenCode/agent để skill mới được tải.

Quy tắc chung cho agent nằm trong `AGENTS.md`.

## Lệnh Thường Dùng

```powershell
# Build backend
dotnet build backend/G54.sln -c Release

# Test backend
dotnet test backend/G54.sln -c Release

# Start frontend native
npm --prefix frontend start

# Lint frontend
npm --prefix frontend run lint

# Test frontend
npm --prefix frontend run test:ci

# Build frontend production
npm --prefix frontend run build -- --configuration production

# Xem trạng thái containers
docker compose --env-file docker/.env -f docker/docker-compose.yml ps

# Xem logs
docker compose --env-file docker/.env -f docker/docker-compose.yml logs -f

# Xem log một service cụ thể
docker compose --env-file docker/.env -f docker/docker-compose.yml logs -f api

# Validate Compose configuration
docker compose --env-file docker/.env -f docker/docker-compose.yml config --quiet
```

## Troubleshooting

### Docker engine không chạy

Kiểm tra:

```powershell
wsl --version
wsl --status
docker info
```

Nếu Docker Desktop Linux engine pipe không tồn tại:

1. Đảm bảo `Virtual Machine Platform` và `Windows Subsystem for Linux` đã được bật (enabled).
2. Khởi động lại Windows qua Power > Restart.
3. Cập nhật WSL: `winget upgrade --id Microsoft.WSL`.
4. Mở Docker Desktop và chờ đến khi engine running.

### Docker không resolve được registry

Lỗi thường gặp:

```text
lookup registry-1.docker.io: no such host
```

Thử:

```powershell
wsl --shutdown
```

Sau đó restart Docker Desktop và build lại. Kiểm tra VPN/proxy/DNS nếu lỗi vẫn tiếp diễn.

### API unhealthy nhưng vẫn gọi được endpoint

Xem healthcheck và log:

```powershell
docker inspect docker-api-1 --format '{{json .State.Health}}'
docker compose --env-file docker/.env -f docker/docker-compose.yml logs api
```

API image bắt buộc phải có `curl`, vì Compose healthcheck sử dụng `curl /health`.

### DbMigrator thất bại

```powershell
docker compose --env-file docker/.env -f docker/docker-compose.yml logs migrator postgres
```

Nếu credential trong `.env` đã thay đổi sau khi volume được khởi tạo, hãy tạo lại development volume:

```powershell
docker compose --env-file docker/.env -f docker/docker-compose.yml down -v
docker compose --env-file docker/.env -f docker/docker-compose.yml up --build
```

### Port đã được sử dụng

Kiểm tra các port `4200`, `5000`, `5050`, `5432`, `6379`, `8080` và dừng process/container cũ trước khi chạy lại.

### Frontend hiện API unavailable

Kiểm tra:

```powershell
Invoke-RestMethod http://localhost:5000/api/hello
Invoke-WebRequest http://localhost:5000/health/ready
```

Trong full Docker, kiểm tra proxy:

```powershell
Invoke-RestMethod http://localhost:8080/api/hello
```

Kết quả mong đợi:

```json
{
  "message": "Hello World"
}
```

## Trạng Thái Xác Minh

Dự án đã được smoke test với:

- PostgreSQL healthy và kết nối bằng `postgres/postgres` trên database `postgres`.
- Redis trả về `PONG`.
- DbMigrator exit `0`, migration table và seed record tồn tại.
- API `/health` và `/health/ready` trả về `200`.
- API `/api/hello` trả về `Hello World`.
- Frontend nginx trả về `200` và proxy `/api/hello` thành công.
- pgAdmin trả về `200`.
- Backend code review pass.
- Frontend code review pass, `npm audit --omit=dev` không có vulnerability.
