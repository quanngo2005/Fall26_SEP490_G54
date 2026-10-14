# Checklist lỗi môi trường (Docker Compose — 6 service)

Stack: `postgres`, `redis`, `pgadmin`, `migrator`, `api`, `frontend`.

```bash
docker compose --env-file docker/.env -f docker/docker-compose.yml up --build -d
docker compose --env-file docker/.env -f docker/docker-compose.yml ps -a
```

Kết quả mong đợi: `postgres`, `redis`, `api` ở trạng thái `healthy`; `pgadmin`, `frontend` `running`; `migrator` `exited (0)`.

| URL | Service |
| --- | --- |
| http://localhost:8080/login | frontend |
| http://localhost:5000/health · /swagger | api |
| http://localhost:5050 | pgAdmin |
| localhost:5432 / localhost:6379 | postgres / redis |

## Checklist trước khi chạy

- [ ] Docker Desktop đang chạy (`docker info` không lỗi), WSL2 backend bật (Windows).
- [ ] Đã có `docker/.env` (copy từ `docker/.env.example`) và đặt `JWT_KEY` ≥ 32 ký tự.
- [ ] Các port 5432, 6379, 5050, 5000, 8080 chưa bị chiếm.
- [ ] Docker có ≥ 4 GB RAM và đủ dung lượng đĩa (`docker system df`).

## Lỗi thường gặp

| Triệu chứng | Nguyên nhân | Cách xử lý |
| --- | --- | --- |
| `required variable JWT_KEY is missing a value` | Thiếu `docker/.env` hoặc thiếu `JWT_KEY` | Copy `.env.example` → `.env`, sinh key: `openssl rand -base64 48`. Luôn truyền `--env-file docker/.env`. |
| API exit: `Jwt:Key must contain at least 256 bits` | Key ngắn hơn 32 byte | Sinh lại key dài hơn. |
| API exit: `A non-development Jwt:Key must be provided` | Dùng key placeholder với overlay production | Đặt key ngẫu nhiên thật trong `.env` / secret store. |
| `Bind for 0.0.0.0:5432 failed: port is already allocated` | PostgreSQL/Redis cài trên máy hoặc container khác đang chạy | Dừng service local (`services.msc` → postgresql) hoặc `docker ps` rồi stop container trùng port. |
| `ports are not available: ... 0.0.0.0:8080 ... Only one usage of each socket address` (frontend `Created`) | Dịch vụ hệ thống chiếm 8080 (đã gặp: `AgentService`) | Đặt `FRONTEND_PORT=8081` trong `docker/.env` rồi `up -d`; truy cập http://localhost:8081. |
| `frontend` `Exited (127)`, log `40-replace-env.sh: not found` | `frontend/scripts/replace_env.sh` bị CRLF trong working copy (shebang `#!/bin/sh\r`) | Dockerfile đã tự strip `\r`; build lại `up -d --build frontend`. |
| `port 5000/8080 in use` | API native (`run.ps1`) hoặc app khác đang chạy | `.\run.ps1 -Stop`, hoặc `Get-NetTCPConnection -LocalPort 5000` để tìm PID. |
| `migrator` exit ≠ 0, log `password authentication failed` | Đổi `POSTGRES_PASSWORD` sau khi volume đã khởi tạo | `docker compose ... down -v` rồi `up --build` (mất dữ liệu dev). |
| `api` không chạy, `dependency failed to start: migrator` | Migration lỗi | `docker compose ... logs migrator postgres`. |
| `api` `unhealthy`, `/health` báo Redis/DB lỗi | Redis/Postgres chưa sẵn sàng hoặc sai connection string | `docker compose ... logs api redis postgres`; kiểm tra biến `ConnectionStrings__*`. |
| `frontend` không lên (chờ `api` healthy) | API unhealthy | Sửa API trước; frontend phụ thuộc `service_healthy`. |
| Build .NET lỗi `NU1603`/`NU1102` | Version package không tồn tại trên NuGet (warnings-as-errors) | Kiểm tra version trong `*.csproj`; Npgsql EF provider 8.x dừng ở 8.0.11. |
| Build lỗi mạng khi `dotnet restore` / `npm ci` | Proxy / DNS / mất mạng | Kiểm tra mạng, cấu hình proxy trong Docker Desktop, chạy lại `up --build`. |
| `dotnet format` báo `ENDOFLINE` trên Windows | Working copy checkout CRLF trước khi có `.gitattributes` | `git rm --cached -r . && git reset --hard` (sau khi commit hết thay đổi) để checkout lại theo `eol=lf`. |
| Script `.sh` lỗi `\r: command not found` trong container | File shell bị CRLF | `.gitattributes` đã ép `*.sh eol=lf`; checkout lại file. |
| pgAdmin không login được | Sai `PGADMIN_EMAIL`/`PGADMIN_PASSWORD` hoặc volume cũ | Kiểm tra `.env`; host kết nối DB trong pgAdmin là `postgres`, không phải `localhost`. |
| Hết dung lượng đĩa | Image/build cache tích luỹ | `docker system prune` (cẩn thận: xoá image/container không dùng). |

## Lệnh chẩn đoán nhanh

```bash
docker compose --env-file docker/.env -f docker/docker-compose.yml config --quiet
docker compose --env-file docker/.env -f docker/docker-compose.yml ps -a
docker compose --env-file docker/.env -f docker/docker-compose.yml logs -f api
curl http://localhost:5000/health
```
