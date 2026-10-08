# OnlineSurvey

Hệ thống tạo và quản lý khảo sát trực tuyến bằng ASP.NET Core MVC .NET 8. Quản trị viên tạo form động, xuất bản đường dẫn công khai, nhận phản hồi, xem thống kê và xuất CSV. MongoDB lưu dữ liệu; Redis cache form và giới hạn lượt gửi.

README này là tài liệu đầy đủ để cài đặt, kết nối database, chạy, sử dụng, xử lý lỗi, build và push dự án lên GitHub.

## 1. Chức năng

- Đăng nhập quản trị bằng tài khoản lưu trong MongoDB.
- Tạo, sửa, xóa, xuất bản hoặc đóng khảo sát.
- Bốn loại câu hỏi: ngắn, dài, chọn một, chọn nhiều.
- Kiểm tra dữ liệu ở server.
- Form công khai tại `/surveys/{slug}`.
- Lưu phản hồi, xem thống kê và xuất CSV.
- Redis cache survey Published 5 phút.
- Redis giới hạn 3 lượt gửi/phút/client/khảo sát.
- Cookie hạn chế gửi lặp trong 10 phút.

## 2. Công nghệ

| Thành phần | Công nghệ |
|---|---|
| Backend | C#, ASP.NET Core MVC .NET 8 |
| Frontend | Razor, Bootstrap, CSS, JavaScript |
| Database | MongoDB |
| Cache/rate limit | Redis |
| Authentication | ASP.NET Core Cookie |
| Packages | MongoDB.Driver 3.11.2, StackExchange.Redis 3.2.1 |

## 3. Kiến trúc

~~~text
Browser
  |
ASP.NET Core MVC
  |-- AccountController ---------> MongoAdminRepository -> admins
  |-- AdminSurveysController ----> SurveyService
  |-- SurveysController ---------> SurveyService
                                     |-- MongoSurveyRepository
                                     |     |-- surveys
                                     |     └-- responses
                                     └-- Redis cache/rate limit
~~~

~~~text
OnlineSurvey/
├── OnlineSurvey.slnx
├── start-dependencies.ps1
├── README.md
├── .gitignore
├── database/
│   ├── OnlineSurvey-Schema.mongodb.js
│   └── OnlineSurvey-Database-Day-Du.mongodb.js
└── OnlineSurvey/
    ├── OnlineSurvey.csproj
    ├── Program.cs
    ├── appsettings.json
    ├── Controllers/
    ├── Infrastructure/
    ├── Models/
    ├── Services/
    ├── Views/
    └── wwwroot/
~~~

## 4. Yêu cầu cài đặt

- .NET 8 SDK.
- Visual Studio 2022 hỗ trợ .NET 8 hoặc VS Code.
- Git nếu dùng GitHub.
- MongoDB Community Server hoặc MongoDB portable.
- Redis Server tương thích chạy trực tiếp trên máy.
- MongoDB Compass nếu muốn xem database bằng giao diện.

~~~powershell
dotnet --version
dotnet --list-sdks
git --version
~~~

Project target `net8.0`. Nếu Visual Studio báo không hỗ trợ, hãy cập nhật Visual Studio 2022 và cài .NET 8 SDK.

## 5. Chạy nhanh trên Windows

Sau khi đã cài MongoDB và Redis, mở PowerShell tại thư mục chứa `OnlineSurvey.slnx`:

~~~powershell
Set-ExecutionPolicy -Scope Process Bypass
.\start-dependencies.ps1
Test-NetConnection 127.0.0.1 -Port 27017
Test-NetConnection 127.0.0.1 -Port 6379
~~~

Hai kết quả `TcpTestSucceeded` cần là `True`.

Khởi động ứng dụng như bình thường:

~~~powershell
dotnet restore .\OnlineSurvey.slnx
dotnet run --project .\OnlineSurvey\OnlineSurvey.csproj --launch-profile http
~~~

Nếu database chưa có admin, ứng dụng tự tạo tài khoản mặc định `admin / Admin@12345`. Tài khoản chỉ được tạo khi collection `admins` còn rỗng; ứng dụng không ghi đè tài khoản đã có.

Admin được lưu trong MongoDB và mật khẩu chỉ được lưu dưới dạng hash. Từ lần chạy tiếp theo, chỉ cần bật MongoDB, Redis rồi chạy:

~~~powershell
dotnet run --project .\OnlineSurvey\OnlineSurvey.csproj --launch-profile http
~~~

Đăng nhập bằng tài khoản đó. Đây là tài khoản mặc định cho đồ án/demo; hãy đổi mật khẩu trước khi triển khai công khai.

## 6. Cài và chạy MongoDB, Redis trực tiếp

Cổng mặc định:

~~~text
MongoDB  127.0.0.1:27017
Redis    127.0.0.1:6379
HTTP     127.0.0.1:5080
HTTPS    127.0.0.1:7124
~~~

Project có script cho bản portable trên Windows:

~~~powershell
Set-ExecutionPolicy -Scope Process Bypass
.\start-dependencies.ps1
~~~

Script tự tìm `mongod.exe` và `redis-server.exe` trong `PATH`, thư mục cài đặt phổ biến và `%LOCALAPPDATA%`. Nếu không tìm thấy, thông báo lỗi sẽ chỉ rõ dịch vụ còn thiếu.

## 7. Cấu hình kết nối

`OnlineSurvey/appsettings.json`:

~~~json
{
  "MongoDb": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "OnlineSurvey"
  },
  "Redis": {
    "ConnectionString": "127.0.0.1:6379"
  },
  "AuthDatabase": {
    "DatabaseName": "OnlineSurvey"
  }
}
~~~

Ba collection đều nằm trong database `OnlineSurvey`. Project không dùng `OnlineSurveyAuth`.

Ghi đè bằng biến môi trường:

~~~powershell
$env:MongoDb__ConnectionString = "mongodb://127.0.0.1:27017"
$env:MongoDb__DatabaseName = "OnlineSurvey"
$env:AuthDatabase__DatabaseName = "OnlineSurvey"
$env:Redis__ConnectionString = "127.0.0.1:6379"
~~~

MongoDB Atlas:

~~~powershell
$env:MongoDb__ConnectionString = "mongodb+srv://USER:PASSWORD@CLUSTER/OnlineSurvey?retryWrites=true&w=majority"
$env:MongoDb__DatabaseName = "OnlineSurvey"
$env:AuthDatabase__DatabaseName = "OnlineSurvey"
~~~

Không commit connection string có mật khẩu lên GitHub.

## 8. Tài khoản admin

Khi khởi động, app tự tạo các index duy nhất cho tên tài khoản và tài khoản khởi tạo. Nếu collection `admins` chưa có tài khoản, ứng dụng tạo tài khoản mặc định `admin / Admin@12345`. Mật khẩu được băm bằng `PasswordHasher<AdminAccount>` trước khi lưu; database không lưu mật khẩu dạng chữ thường. Ứng dụng không ghi đè tài khoản khi collection đã có admin.

Tài khoản được giữ trong MongoDB, vì vậy những lần khởi động tiếp theo chỉ cần chạy ứng dụng rồi đăng nhập. Tài khoản mặc định phù hợp cho đồ án chạy local; không dùng mật khẩu mặc định trên hệ thống công khai.

## 9. Chạy bằng CLI hoặc Visual Studio

CLI:

~~~powershell
dotnet restore .\OnlineSurvey.slnx
dotnet build .\OnlineSurvey.slnx --configuration Debug
dotnet run --project .\OnlineSurvey\OnlineSurvey.csproj --launch-profile http
~~~

Visual Studio:

1. Mở `OnlineSurvey.slnx`.
2. Chọn profile `http` hoặc `https`.
3. Bật MongoDB và Redis.
4. Nhấn F5 hoặc Ctrl+F5.

Nếu HTTPS lỗi:

~~~powershell
dotnet dev-certs https --clean
dotnet dev-certs https --trust
~~~

## 10. URL chính

| URL | Quyền | Chức năng |
|---|---|---|
| `/` | Công khai | Trang chủ |
| `/account/login` | Công khai | Đăng nhập |
| `/admin/surveys` | Admin | Danh sách |
| `/admin/surveys/create` | Admin | Tạo survey |
| `/admin/surveys/{id}/edit` | Admin | Sửa |
| `/admin/surveys/{id}/results` | Admin | Kết quả |
| `/admin/surveys/{id}/export` | Admin | Xuất CSV |
| `/surveys/{slug}` | Công khai | Trả lời |

## 11. Quy trình sử dụng

### Admin

1. Đăng nhập tại `/account/login`.
2. Tạo survey và thêm ít nhất một câu hỏi.
3. Câu hỏi lựa chọn cần tối thiểu hai đáp án.
4. Lưu Draft, sau đó Xuất bản.
5. Chia sẻ `/surveys/{slug}`.
6. Xem kết quả hoặc tải CSV.
7. Chuyển về Draft để ngừng nhận phản hồi.

### Người trả lời

1. Mở link Published.
2. App đọc Redis; cache miss thì đọc MongoDB và cache 5 phút.
3. Server kiểm tra question id, option id, required và độ dài.
4. Redis kiểm tra giới hạn 3 lượt/phút.
5. Phản hồi hợp lệ được ghi vào `responses`.
6. Cookie chống gửi lại tồn tại 10 phút.

## 12. MongoDB

File schema dễ đọc, tương đương phần `CREATE TABLE` của SQL:

~~~powershell
mongosh "mongodb://127.0.0.1:27017/OnlineSurvey" --file ".\database\OnlineSurvey-Schema.mongodb.js"
~~~

File này không xóa và không chèn dữ liệu. Nó tạo hoặc cập nhật ba collection, JSON Schema validator và index. File `OnlineSurvey-Database-Day-Du.mongodb.js` là bản backup có đầy đủ dữ liệu mẫu.

~~~text
OnlineSurvey
├── admins
├── surveys
└── responses
~~~

~~~text
admins 1 -------- N surveys 1 -------- N responses
          CreatedByAdminId      SurveyId
~~~

- `admins._id -> surveys.CreatedByAdminId`: 1-N.
- `surveys._id -> responses.SurveyId`: 1-N.
- `Questions[]` và `Options[]` nhúng trong survey.
- `Answers[]` nhúng trong response.
- MongoDB không tạo foreign key vật lý; code quản lý quan hệ.
- Xóa survey sẽ xóa response cùng `SurveyId`.

### admins

~~~json
{
  "_id": "ObjectId",
  "Username": "admin",
  "NormalizedUsername": "ADMIN",
  "PasswordHash": "PBKDF2 hash",
  "DisplayName": "Quản trị viên",
  "Role": "Admin",
  "IsActive": true,
  "CreatedAt": "DateTime UTC",
  "LastLoginAt": "DateTime UTC"
}
~~~

### surveys

~~~json
{
  "_id": "ObjectId",
  "CreatedByAdminId": "ObjectId -> admins._id",
  "Slug": "khao-sat-mau",
  "Title": "Khảo sát mẫu",
  "Description": "Mô tả",
  "Status": "Draft hoặc Published",
  "Version": 1,
  "Questions": [
    {
      "Id": "question-id",
      "Order": 1,
      "Type": "text | textarea | single_choice | multiple_choice",
      "Text": "Nội dung",
      "Required": true,
      "Options": [{ "Id": "option-id", "Text": "Đáp án" }]
    }
  ],
  "CreatedAt": "DateTime UTC",
  "UpdatedAt": "DateTime UTC"
}
~~~

Index `Slug_1` là unique.

### responses

~~~json
{
  "_id": "ObjectId",
  "SurveyId": "ObjectId -> surveys._id",
  "SurveyVersion": 1,
  "RespondentName": "Nguyễn Văn A",
  "Answers": [
    {
      "QuestionId": "question-id",
      "Values": ["option-id hoặc câu trả lời"]
    }
  ],
  "SubmittedAt": "DateTime UTC"
}
~~~

Index ghép: `SurveyId ASC, SubmittedAt DESC`.

## 13. Redis

~~~text
Cache key: survey:published:{slug}
TTL:       5 phút

Rate key:  rate:survey:{surveyId}:{clientKey}
Giới hạn:  3 lượt/phút/client/khảo sát
TTL:       1 phút
~~~

`clientKey` là hash SHA-256 của IP và User-Agent. Nếu Redis lỗi, app fallback MongoDB và ghi cảnh báo. Production cần giám sát Redis.

## 14. MongoDB Compass

1. Mở Compass.
2. Kết nối `mongodb://127.0.0.1:27017`.
3. Mở database `OnlineSurvey`.
4. Kiểm tra `admins`, `surveys` và `responses`.
5. Nếu chưa thấy database, chạy app và tạo dữ liệu trước.

~~~javascript
use OnlineSurvey
show collections
db.admins.countDocuments()
db.surveys.countDocuments()
db.responses.countDocuments()
db.admins.find({}, { Username: 1, Role: 1, IsActive: 1 })
db.surveys.find({}, { Title: 1, Slug: 1, Status: 1, CreatedByAdminId: 1 })
~~~

## 15. Dữ liệu mẫu và backup

Dữ liệu trong MongoDB không tự đi theo source code. Repository đã kèm script mẫu chứa 1 admin, 15 khảo sát và 70 phản hồi để máy mới có thể phục hồi nhanh.

Lưu ý: script sẽ xóa ba collection `admins`, `surveys`, `responses` trong database `OnlineSurvey` hiện tại trước khi chèn dữ liệu mẫu. Chỉ chạy khi chấp nhận thay dữ liệu local hiện có.

Khôi phục bằng mongosh:

~~~powershell
mongosh "mongodb://127.0.0.1:27017/OnlineSurvey" .\database\OnlineSurvey-Database-Day-Du.mongodb.js
~~~

Sau khi chạy, đăng nhập bằng `admin / Admin@12345`, rồi kiểm tra số lượng:

~~~javascript
use OnlineSurvey
db.admins.countDocuments()    // 1
db.surveys.countDocuments()   // 15
db.responses.countDocuments() // 70
~~~

Nếu tự tạo backup BSON bằng mongodump, có thể khôi phục bằng:

~~~powershell
mongorestore --uri="mongodb://127.0.0.1:27017" --drop .\database\dump
~~~

Không đưa backup chứa dữ liệu cá nhân hoặc credential thật lên repository public.

## 16. Xử lý lỗi

~~~powershell
Test-NetConnection 127.0.0.1 -Port 27017
Test-NetConnection 127.0.0.1 -Port 6379
Get-NetTCPConnection -LocalPort 27017,6379,5080,7124 -ErrorAction SilentlyContinue
~~~

Nếu không đăng nhập được:

- Kiểm tra database là `OnlineSurvey`.
- Kiểm tra `admins` có document.
- Nếu `admins` rỗng, app tự tạo `admin / Admin@12345`.
- Nếu `admins` đã có tài khoản, app không ghi đè; đăng nhập bằng tài khoản đang có.

## 17. Dừng MongoDB và Redis local

~~~powershell
Get-Process mongod, redis-server -ErrorAction SilentlyContinue
Stop-Process -Name mongod, redis-server
~~~

Lệnh dừng process không xóa dữ liệu. MongoDB portable của script lưu dữ liệu trong `%LOCALAPPDATA%\MongoDB\data\OnlineSurvey`.

## 18. Build và publish

~~~powershell
dotnet restore .\OnlineSurvey.slnx
dotnet build .\OnlineSurvey.slnx --configuration Release
dotnet publish .\OnlineSurvey\OnlineSurvey.csproj --configuration Release --runtime win-x64 --self-contained true --output .\publish\win-x64
~~~

Publish không đóng gói MongoDB/Redis.

## 19. Push GitHub

`.gitignore` loại `.vs`, `bin`, `obj`, file user, log và dữ liệu local.

~~~powershell
git init
git branch -M main
git add .
git commit -m "Initial OnlineSurvey project"
git remote add origin https://github.com/USERNAME/REPOSITORY.git
git push -u origin main
~~~

Máy khác:

~~~powershell
git clone https://github.com/USERNAME/REPOSITORY.git
cd REPOSITORY
Set-ExecutionPolicy -Scope Process Bypass
.\start-dependencies.ps1
dotnet restore .\OnlineSurvey.slnx
dotnet run --project .\OnlineSurvey\OnlineSurvey.csproj --launch-profile http
~~~

Trên database mới, ứng dụng tự tạo `admin / Admin@12345` khi khởi động. Các lần sau chỉ cần bật dịch vụ và chạy `dotnet run --project .\OnlineSurvey\OnlineSurvey.csproj --launch-profile http`, sau đó đăng nhập bằng tài khoản này.

## 20. Checklist trước khi nộp/demo

- [ ] Build 0 error và 0 warning.
- [ ] MongoDB cổng 27017, Redis cổng 6379 hoạt động.
- [ ] `OnlineSurvey` có 3 collection.
- [ ] Đăng nhập admin thành công.
- [ ] Tạo được 4 loại câu hỏi.
- [ ] Xuất bản và mở link công khai.
- [ ] Server từ chối dữ liệu không hợp lệ.
- [ ] Gửi response và thấy trong Compass.
- [ ] Xem thống kê và xuất CSV.
- [ ] Cache và rate limit hoạt động.
- [ ] Không commit secret, `.vs`, `bin`, `obj`.
- [ ] Đổi mật khẩu demo trước khi public.

## 21. Trạng thái dự án

Project có đầy đủ MVC source, UI Razor, đăng nhập admin, form builder, MongoDB repository, Redis cache/rate limit, thống kê và CSV.

~~~powershell
dotnet build .\OnlineSurvey.slnx --configuration Release
~~~
