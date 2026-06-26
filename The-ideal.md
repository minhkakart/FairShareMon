Dưới đây là tài liệu thiết kế chi tiết hệ thống back-end cho ứng dụng **"Sổ ghi nợ chi tiêu"** được trình bày dưới dạng Markdown.

📑 TÀI LIỆU THIẾT KẾ BACK-END: HỆ THỐNG "SỔ GHI NỢ CHI TIÊU"
============================================================

1\. Tổng quan Hệ thống (System Overview)
----------------------------------------

Hệ thống cung cấp API cho ứng dụng quản lý chi tiêu cá nhân/nhóm. Điểm cốt lõi của hệ thống là tính cá nhân hóa cao (Resource Owned), bảo mật cơ bản bằng Whitelist Token, và khả năng quản lý linh hoạt các khoản chi tiêu theo phiếu và theo đợt.

Mục tiêu nghiệp vụ là **chia tiền & ghi nợ**: với mỗi phiếu chi tiêu có một người **đứng ra trả (payer)** và nhiều thành viên **gánh một phần (record)**. Từ đó hệ thống tính được **ai đang nợ / ai cho nợ** bao nhiêu.

Ngoài ra hệ thống hỗ trợ **phân loại chi tiêu** (mỗi phiếu có 1 **Category** bắt buộc + nhiều **Tag** tùy chọn) để phục vụ biểu đồ thống kê, và **Audit Logging** ghi lại toàn bộ lịch sử thay đổi của phiếu & record (ai sửa, lúc nào, giá trị cũ/mới) nhằm tránh tranh cãi.

**Tech stack (đã chốt):**

*   **Ngôn ngữ/Framework:** ASP.NET Core Web API (.NET 8, C#).
    
*   **ORM:** Entity Framework Core (provider Pomelo/MySQL).
    
*   **Cơ sở dữ liệu (Database):** **MySQL / MariaDB** (RDBMS phù hợp với dữ liệu có quan hệ chặt chẽ).
    
*   **Caching/Session:** **Memcached** (lưu Whitelist Token để truy xuất nhanh, kèm TTL = thời gian hết hạn token). Fallback xác thực vẫn dùng DB nếu cache miss.
    

2\. Thiết kế Cơ sở dữ liệu (Database Schema)
--------------------------------------------

Hệ thống xoay quanh nguyên tắc **Resource Owned**, do đó mọi bảng dữ liệu chính (ngoại trừ bảng User) đều phải có khóa ngoại `user_id` để phân quyền truy cập. Tất cả các bảng đều có thêm `updated_at` phục vụ audit & đồng bộ.

### 2.1. Các bảng dữ liệu (Tables)

**1\. `users` (Người dùng)**

*   `id`: UUID (Primary Key)
    
*   `username`: Varchar (Unique)
    
*   `password_hash`: Varchar
    
*   `created_at`: Timestamp
    
*   `updated_at`: Timestamp
    

**2\. `auth_tokens` (Whitelist Token — opaque, stateful)**

*   `id`: UUID (Primary Key)
    
*   `user_id`: UUID (Foreign Key -> `users.id`)
    
*   `token_hash`: Varchar — **lưu HASH (SHA-256) của token, KHÔNG lưu token gốc**
    
*   `type`: Enum (`ACCESS`, `REFRESH`)
    
*   `expires_at`: Timestamp
    
*   `created_at`: Timestamp
    

**3\. `members` (Thành viên)**

*   `id`: UUID (Primary Key)
    
*   `user_id`: UUID (Foreign Key -> `users.id`) - _Đảm bảo Resource Owned_
    
*   `name`: Varchar
    
*   `is_owner`: Boolean (Default: false) - _Đánh dấu thành viên đại diện cho chính chủ tài khoản (creator). Mỗi user có đúng 1 member `is_owner = true`, tạo tự động khi register._
    
*   `is_active`: Boolean (Default: true) - _Dùng cho Soft Delete_
    
*   `created_at`: Timestamp
    
*   `updated_at`: Timestamp
    

**4\. `expense_batches` (Đợt chi tiêu)**

*   `id`: UUID (Primary Key)
    
*   `user_id`: UUID (Foreign Key -> `users.id`)
    
*   `name`: Varchar
    
*   `status`: Enum (`OPEN`, `CLOSED`) - _Default: OPEN_
    
*   `start_date`: Timestamp
    
*   `end_date`: Timestamp (Nullable)
    
*   `created_at`: Timestamp
    
*   `updated_at`: Timestamp
    

**5\. `expense_vouchers` (Phiếu chi tiêu)**

*   `id`: UUID (Primary Key)
    
*   `user_id`: UUID (Foreign Key -> `users.id`)
    
*   `batch_id`: UUID (Foreign Key -> `expense_batches.id`, Nullable)
    
*   `payer_member_id`: UUID (Foreign Key -> `members.id`) - _Người đứng ra trả. **Mặc định = member `is_owner` của user (creator)**, có thể đổi sau. Bắt buộc cùng `user_id` với phiếu._
    
*   `category_id`: UUID (Foreign Key -> `categories.id`) - **NOT NULL**. _Mỗi phiếu thuộc đúng 1 category. Nếu client không gửi → gán category mặc định (`is_default = true`) của user. Bắt buộc cùng `user_id` với phiếu._
    
*   `name`: Varchar
    
*   `description`: Text
    
*   `expense_time`: Timestamp
    
*   `created_at`: Timestamp
    
*   `updated_at`: Timestamp
    

**6\. `voucher_records` (Chi tiết phần gánh của thành viên)**

*   `id`: UUID (Primary Key)
    
*   `user_id`: UUID (Foreign Key -> `users.id`) - _Thêm để áp dụng Resource Owned đồng nhất; phải khớp `user_id` của voucher._
    
*   `voucher_id`: UUID (Foreign Key -> `expense_vouchers.id`)
    
*   `member_id`: UUID (Foreign Key -> `members.id`)
    
*   `amount`: Decimal/Numeric — **CHECK (`amount >= 0`)**. Phần tiền thành viên này phải gánh.
    
*   `note`: Text (Ghi chú chi tiết)
    
*   `created_at`: Timestamp
    
*   `updated_at`: Timestamp
    

**7\. `categories` (Danh mục chi tiêu)**

*   `id`: UUID (Primary Key)
    
*   `user_id`: UUID (Foreign Key -> `users.id`) - _Resource Owned_
    
*   `name`: Varchar (ví dụ: Ăn uống, Đi lại, Khách sạn, Mua sắm)
    
*   `color`: Varchar (Nullable) - _Mã màu cho biểu đồ, ví dụ `#FF8800`_
    
*   `icon`: Varchar (Nullable) - _Tên icon hiển thị (tùy chọn)_
    
*   `is_default`: Boolean (Default: false) - _Mỗi user có đúng 1 category mặc định (gán cho phiếu khi client không chọn). Không cho xóa category mặc định._
    
*   `is_active`: Boolean (Default: true) - _Soft Delete; không xóa cứng vì còn ràng buộc với phiếu._
    
*   `created_at`: Timestamp
    
*   `updated_at`: Timestamp
    
*   _Ràng buộc: `UNIQUE (user_id, name)` (theo các bản ghi `is_active = true`)._
    

**8\. `tags` (Nhãn tự do)**

*   `id`: UUID (Primary Key)
    
*   `user_id`: UUID (Foreign Key -> `users.id`) - _Resource Owned_
    
*   `name`: Varchar
    
*   `is_active`: Boolean (Default: true) - _Soft Delete_
    
*   `created_at`: Timestamp
    
*   `updated_at`: Timestamp
    
*   _Ràng buộc: `UNIQUE (user_id, name)`._
    

**9\. `voucher_tags` (Bảng nối phiếu ↔ tag, Many-to-Many)**

*   `voucher_id`: UUID (Foreign Key -> `expense_vouchers.id`)
    
*   `tag_id`: UUID (Foreign Key -> `tags.id`)
    
*   `user_id`: UUID (Foreign Key -> `users.id`) - _Resource Owned; phải khớp `user_id` của cả voucher và tag._
    
*   `created_at`: Timestamp
    
*   _Khóa chính tổ hợp: `PRIMARY KEY (voucher_id, tag_id)`. Xóa phiếu hoặc tag → cascade xóa dòng nối._
    

**10\. `audit_logs` (Nhật ký thay đổi — Vouchers & Records)**

*   `id`: UUID (Primary Key)
    
*   `user_id`: UUID (Foreign Key -> `users.id`) - _Người thực hiện (actor) & chủ sở hữu resource_
    
*   `entity_type`: Enum (`VOUCHER`, `VOUCHER_RECORD`) - _Phạm vi audit chỉ gồm phiếu và record_
    
*   `entity_id`: UUID - _ID của phiếu/record bị tác động (không đặt FK cứng để giữ được log sau khi resource bị xóa)_
    
*   `action`: Enum (`CREATE`, `UPDATE`, `DELETE`)
    
*   `old_values`: JSON (Nullable) - _Snapshot trước thay đổi (NULL khi `CREATE`)_
    
*   `new_values`: JSON (Nullable) - _Snapshot sau thay đổi (NULL khi `DELETE`)_
    
*   `created_at`: Timestamp - _Thời điểm thay đổi (append-only, không có `updated_at`)_
    
*   _Index gợi ý: `(user_id, entity_type, entity_id, created_at)` để truy vấn lịch sử 1 phiếu nhanh._
    

### 2.2. Ràng buộc toàn vẹn (Cross-user constraints)

*   `expense_vouchers.payer_member_id` → member phải thuộc cùng `user_id`.
    
*   `voucher_records.member_id` → member phải thuộc cùng `user_id` với voucher (chặn gán record cho member của user khác).
    
*   `voucher_records.user_id` = `expense_vouchers.user_id` của `voucher_id` tương ứng.
    
*   `expense_vouchers.category_id` → category phải thuộc cùng `user_id` và `is_active = true`.
    
*   `voucher_tags.tag_id` & `voucher_tags.voucher_id` → cả hai phải thuộc cùng `user_id` với dòng nối.
    
*   `amount >= 0` (CHECK constraint ở DB, không chỉ validate ở app).
    
*   `audit_logs` là **append-only**: không cho `UPDATE`/`DELETE` (chỉ INSERT), kể cả khi resource gốc bị xóa.
    

3\. Kiến trúc Bảo mật & Phân quyền
----------------------------------

### 3.1. Authentication (Stateful Opaque Token)

*   **Cơ chế:** Dùng **opaque token** (chuỗi random đủ entropy, ví dụ 256-bit base64url), **KHÔNG dùng JWT** — vì hệ thống đã stateful (check whitelist mỗi request) nên JWT không mang lại lợi ích stateless mà chỉ tăng độ phức tạp.
    
*   **Lưu trữ:** Khi login thành công, server sinh token, **hash (SHA-256) rồi lưu hash** vào `auth_tokens` (đây là Whitelist) và đẩy vào Memcached với TTL = `expires_at`. Token gốc chỉ trả về client 1 lần.
    
*   **Validate:** Mỗi request, middleware lấy token từ Header → hash → kiểm tra trong Memcached (miss thì fallback DB). Hợp lệ + còn hạn + tồn tại trong whitelist mới cho qua.
    
*   **Access + Refresh token:** Cấp cặp `ACCESS` (TTL ngắn) + `REFRESH` (TTL dài). Endpoint refresh đổi access token mới; có thể xoay vòng refresh token.
    
*   **Logout/Revoke:** Xóa token khỏi whitelist (DB + cache). Đổi mật khẩu → xóa toàn bộ token của user để buộc đăng nhập lại mọi thiết bị.
    

### 3.2. Authorization (Resource Owned)

*   **Global Middleware:** Bắt mọi request có chứa `id` của resource (ví dụ: `GET /vouchers/:id`).
    
*   **Logic:** Query resource đó kèm điều kiện `WHERE id = :id AND user_id = :current_user_id`. Nếu không tìm thấy, trả về `404 Not Found` (tránh trả `403` để không rò rỉ thông tin ID có tồn tại hay không).
    
*   **Với `voucher_records`:** kiểm tra ownership qua `user_id` của chính record (đã thêm ở 2.1) hoặc JOIN về voucher.
    

4\. Thiết kế API Endpoints (RESTful)
------------------------------------

> Các endpoint list (`GET`) đều hỗ trợ **phân trang `?limit=&offset=`** (mặc định limit 20, max 100) và trả về tổng số bản ghi.

### Auth & User

*   `POST /api/auth/register`: Đăng ký user mới; tự tạo member `is_owner = true` tương ứng.
    
*   `POST /api/auth/login`: Đăng nhập, cấp cặp access/refresh token & lưu whitelist.
    
*   `POST /api/auth/refresh`: Dùng refresh token để cấp access token mới.
    
*   `POST /api/auth/logout`: Xóa token khỏi whitelist (DB + cache).
    

### Members

*   `GET /api/members`: Danh sách thành viên của User. Mặc định chỉ trả `is_active = true`; thêm `?include_inactive=true` để lấy cả thành viên đã xóa (dùng cho thống kê/export).
    
*   `POST /api/members`: Thêm thành viên mới.
    
*   `PUT /api/members/:id`: Cập nhật thông tin.
    
*   `DELETE /api/members/:id`: **Soft Delete** (`is_active = false`) để không ảnh hưởng dữ liệu lịch sử.
    

### Categories (Danh mục)

*   `GET /api/categories`: Danh sách category của User (mặc định `is_active = true`; `?include_inactive=true` để xem cả đã xóa).
    
*   `POST /api/categories`: Tạo category mới.
    
*   `PUT /api/categories/:id`: Cập nhật `name` / `color` / `icon`.
    
*   `POST /api/categories/:id/default`: Đặt category này làm mặc định (bỏ cờ `is_default` của category cũ — trong 1 transaction).
    
*   `DELETE /api/categories/:id`: **Soft Delete** (`is_active = false`). Chặn nếu là category mặc định; phiếu đang dùng vẫn giữ liên kết lịch sử.
    

### Tags (Nhãn)

*   `GET /api/tags`: Danh sách tag của User.
    
*   `POST /api/tags`: Tạo tag mới (chuẩn hóa & chống trùng theo `UNIQUE (user_id, name)`).
    
*   `PUT /api/tags/:id`: Đổi tên tag.
    
*   `DELETE /api/tags/:id`: **Soft Delete** (`is_active = false`); các dòng `voucher_tags` liên quan được gỡ.
    

### Expense Vouchers (Phiếu chi tiêu)

*   `GET /api/vouchers`: List phiếu + phân trang. Filter theo `batch_id`, khoảng thời gian, `category_id`, `tag_id`.
    
*   `POST /api/vouchers`: Tạo phiếu trong **một DB Transaction**. Payload kèm mảng `records`, `category_id` (tùy chọn → mặc định category `is_default`), và mảng `tag_ids` (tùy chọn). **Mặc định:** nếu không truyền `payer_member_id` thì gán = member `is_owner`; đồng thời **tự chèn 1 record cho member creator (`is_owner`) với `amount = 0`**. Ghi `audit_logs` (action `CREATE`) cho phiếu trong cùng transaction.
    
*   `PUT /api/vouchers/:id`: Cập nhật **thông tin chung của phiếu**: `name`, `description`, `payer_member_id`, `expense_time`, `category_id`, `tag_ids` (thay thế toàn bộ tập tag). Ghi `audit_logs` (action `UPDATE`, kèm old/new). (Việc thêm/sửa/xóa từng record dùng các endpoint con bên dưới.)
    
*   `DELETE /api/vouchers/:id`: Xóa phiếu (kèm records, trong transaction). Ghi `audit_logs` (action `DELETE`).
    
*   `GET /api/vouchers/:id/export`: Xử lý logic Export phiếu.
    
*   `GET /api/vouchers/:id/audit`: Lấy lịch sử thay đổi của phiếu và các record của nó (đọc `audit_logs` theo `entity_id`), sắp xếp theo `created_at` + phân trang.
    

**Records của phiếu (quản lý riêng để tách bạch với info chung):**

*   `POST /api/vouchers/:id/records`: Thêm record. Ghi `audit_logs` (`VOUCHER_RECORD`, `CREATE`).
    
*   `PUT /api/vouchers/:id/records/:recordId`: Sửa `amount` / `note` / `member_id`. Ghi `audit_logs` (`VOUCHER_RECORD`, `UPDATE`, kèm old/new).
    
*   `DELETE /api/vouchers/:id/records/:recordId`: Xóa record. Ghi `audit_logs` (`VOUCHER_RECORD`, `DELETE`).
    

### Expense Batches (Đợt chi tiêu)

*   `GET /api/batches`: List đợt chi tiêu + phân trang.
    
*   `POST /api/batches`: Tạo đợt mới.
    
*   `PUT /api/batches/:id`: Cập nhật tên/thông tin đợt.
    
*   `POST /api/batches/:id/close`: Kết thúc đợt (Set `status = CLOSED`).
    
*   `POST /api/batches/:id/vouchers`: Thêm phiếu có sẵn vào đợt (Chặn nếu status là `CLOSED`).
    
*   `GET /api/batches/:id/export`: Xử lý logic Export đợt.
    

### Statistics (Thống kê)

*   `GET /api/stats/summary`: Tổng chi tiêu theo khoảng thời gian, và **cân bằng nợ của từng thành viên** (xem 5.3).
    
*   `GET /api/stats/by-category`: Tổng chi tiêu nhóm theo **category** trong khoảng thời gian / theo `batch_id` — phục vụ Pie/Bar Chart (trả về `category_name`, `color`, `total_amount`, `voucher_count`). Xem 5.4.
    

5\. Logic Xử lý Cốt lõi (Core Business Logic)
---------------------------------------------

### 5.1. Logic Thêm/Sửa Phiếu & Đợt

Khi tạo Phiếu mới hoặc thêm Phiếu vào Đợt, backend phải kiểm tra:

1.  Đợt đó có thuộc về `user_id` đang request không.
    
2.  Trạng thái `expense_batches` có đang là `OPEN` không. Nếu `CLOSED` → `400 Bad Request: Đợt chi tiêu đã kết thúc`.
    
3.  **`expense_time` của phiếu phải nằm trong khoảng `[start_date, end_date]` của đợt** (nếu phiếu thuộc đợt). Ngoài khoảng → `400 Bad Request`.
    

**Khóa đợt (CLOSED):** khi đợt đã `CLOSED`, **vô hiệu hóa toàn bộ thao tác ghi** lên các phiếu thuộc đợt đó — chặn `PUT/DELETE /vouchers/:id`, các endpoint records, và thêm phiếu mới vào đợt. Chỉ cho phép đọc/export.

### 5.2. Logic Export Phiếu (Export Voucher)

Dữ liệu đầu ra: Summary tổng tiền mỗi thành viên (phần gánh), concat ghi chú. **SQL giả mã (MySQL/MariaDB):**

```sql
SELECT 
    m.name AS member_name,
    SUM(vr.amount) AS total_amount,
    GROUP_CONCAT(vr.note SEPARATOR ' | ') AS concatenated_notes
FROM voucher_records vr
JOIN members m ON vr.member_id = m.id
WHERE vr.voucher_id = :voucher_id
GROUP BY m.id, m.name;
```

### 5.3. Logic Export Đợt & Cân bằng nợ (Balance)

Tương tự Export phiếu nhưng tính cho toàn bộ phiếu trong đợt, đồng thời tính **cân bằng nợ**.

**Mô hình cân bằng:** với mỗi phiếu, người `payer` ứng trước toàn bộ `SUM(records.amount)`; mỗi thành viên gánh phần `record.amount` của mình. Net của từng member:

*   `paid` = tổng `SUM(records.amount)` của các phiếu mà member đó là `payer`.
    
*   `owed` = tổng `amount` các record của member đó.
    
*   `balance = paid - owed`. **Dương → người khác nợ member này; Âm → member này đang nợ.**
    

**SQL giả mã — phần gánh theo từng phiếu trong đợt:**

```sql
SELECT 
    m.name AS member_name,
    SUM(vr.amount) AS total_owed,
    GROUP_CONCAT(CONCAT(v.name, ': ', vr.note) SEPARATOR ' | ') AS all_notes
FROM voucher_records vr
JOIN expense_vouchers v ON vr.voucher_id = v.id
JOIN members m ON vr.member_id = m.id
WHERE v.batch_id = :batch_id
GROUP BY m.id, m.name;
```

**SQL giả mã — cân bằng nợ trong đợt:**

```sql
SELECT
    m.id AS member_id,
    m.name AS member_name,
    COALESCE(paid.total, 0)  AS paid,
    COALESCE(owed.total, 0)  AS owed,
    COALESCE(paid.total, 0) - COALESCE(owed.total, 0) AS balance
FROM members m
LEFT JOIN (
    SELECT v.payer_member_id AS member_id, SUM(vr.amount) AS total
    FROM expense_vouchers v
    JOIN voucher_records vr ON vr.voucher_id = v.id
    WHERE v.batch_id = :batch_id
    GROUP BY v.payer_member_id
) paid ON paid.member_id = m.id
LEFT JOIN (
    SELECT vr.member_id, SUM(vr.amount) AS total
    FROM voucher_records vr
    JOIN expense_vouchers v ON vr.voucher_id = v.id
    WHERE v.batch_id = :batch_id
    GROUP BY vr.member_id
) owed ON owed.member_id = m.id
WHERE m.user_id = :current_user_id
  AND (paid.total IS NOT NULL OR owed.total IS NOT NULL);
```

_Tip: format output dưới dạng JSON hoặc sinh file Excel/CSV từ backend trả về._

### 5.4. Logic Thống kê theo Category (Charts)

Tổng chi tiêu của mỗi phiếu = `SUM(voucher_records.amount)`; gom theo category. **SQL giả mã (MySQL/MariaDB):**

```sql
SELECT
    c.id   AS category_id,
    c.name AS category_name,
    c.color,
    COUNT(DISTINCT v.id) AS voucher_count,
    COALESCE(SUM(vr.amount), 0) AS total_amount
FROM expense_vouchers v
JOIN categories c       ON v.category_id = c.id
LEFT JOIN voucher_records vr ON vr.voucher_id = v.id
WHERE v.user_id = :current_user_id
  AND (:batch_id IS NULL OR v.batch_id = :batch_id)
  AND (:from IS NULL OR v.expense_time >= :from)
  AND (:to   IS NULL OR v.expense_time <  :to)
GROUP BY c.id, c.name, c.color
ORDER BY total_amount DESC;
```

### 5.5. Logic Audit Logging

Phạm vi audit: **`expense_vouchers` và `voucher_records`**. Mỗi thao tác ghi (CREATE/UPDATE/DELETE) sinh ra 1 dòng `audit_logs` **trong cùng transaction** với thao tác gốc — nếu rollback thì log cũng không được ghi.

*   **CREATE:** `old_values = NULL`, `new_values =` snapshot JSON của bản ghi sau khi tạo.
    
*   **UPDATE:** `old_values =` snapshot trước, `new_values =` snapshot sau. Chỉ ghi khi thực sự có thay đổi (so sánh field) để tránh log rác.
    
*   **DELETE:** `old_values =` snapshot trước khi xóa, `new_values = NULL`.
    
*   **Actor:** `user_id` = người request (hệ thống single-owner nên actor chính là chủ resource).
    
*   **Append-only:** không bao giờ `UPDATE`/`DELETE` bảng `audit_logs`. `entity_id` không đặt FK cứng để log vẫn tồn tại sau khi phiếu/record bị xóa.
    
*   **Snapshot:** chỉ lưu các field nghiệp vụ (ví dụ với record: `member_id`, `amount`, `note`); không lưu dữ liệu nhạy cảm.
    

> Gợi ý triển khai (theo convention dự án): có thể ghi audit tập trung trong tầng Service ngay trong delegate `ExecuteTransactionAsync`, hoặc dùng `SaveChanges` interceptor của EF Core để bắt `ChangeTracker` cho 2 entity này.

6\. Điểm cần chú ý & Nâng cấp (Considerations)
----------------------------------------------

1.  **Toàn vẹn dữ liệu (Data Integrity):** Dùng **Database Transactions** khi tạo phiếu + records và khi xóa phiếu. Lưu records thất bại → rollback toàn bộ.
    
2.  **Xóa Thành viên:** Dùng **Soft Delete** (`is_active = false`) cho `members`. Query để chọn chỉ lấy `is_active = true`; thống kê/export vẫn hiển thị đầy đủ (`?include_inactive=true`).
    
3.  **Xử lý tiền tệ:** Dùng `decimal` (C#) / `DECIMAL` (MySQL), **không dùng `Float`/`double`**. Hoặc lưu số nguyên đơn vị nhỏ nhất (VND).
    
4.  **Tối ưu Whitelist Token:** Lưu whitelist (hash token) trong **Memcached** với TTL = hạn token; fallback DB khi cache miss. Tránh query DB mỗi request.
    
5.  **Xóa Category/Tag:** Dùng **Soft Delete** (`is_active = false`) vì phiếu cũ còn tham chiếu. Không cho xóa category `is_default`. Khi tạo phiếu mà category được chỉ định đã `is_active = false` → `400 Bad Request`.
    
6.  **Seed Category mặc định:** Khi `register`, tạo sẵn vài category gợi ý (Ăn uống, Đi lại, Khách sạn, Mua sắm, Khác) và đánh dấu 1 cái `is_default = true`, để phiếu luôn có category hợp lệ.
    
7.  **Audit tăng trưởng dữ liệu:** `audit_logs` chỉ insert nên sẽ phình theo thời gian; cân nhắc **partition theo tháng** hoặc job archive định kỳ. Index `(user_id, entity_type, entity_id, created_at)` để truy vấn lịch sử nhanh.
    

7\. Tính năng bổ sung & Tương lai (Future Feature Improvements)
---------------------------------------------------------------

> _Audit Logging và Tagging/Categories đã được đưa vào thiết kế cốt lõi (xem mục 2, 4, 5.4, 5.5)._

*   **Chia sẻ Đợt chi tiêu (Read-only Sharing):** Public Link có mã hóa/mật khẩu để thành viên xem báo cáo đợt mà không cần tài khoản.
    
*   **Hỗ trợ đa tiền tệ (Multi-currency):** Lưu thêm tỷ giá tại thời điểm tạo phiếu.
    
*   **Cơ chế Nhắc nợ (Reminders):** Gửi email/Zalo/Telegram tự động cho thành viên còn nợ khi đợt được `CLOSED`.
    
*   **Audit nâng cao:** Mở rộng phạm vi audit sang `members`/`batches`/`categories`, thêm diff field-level và khôi phục (restore) bản ghi từ snapshot.
    
*   **Chuẩn hóa Error Response:** Định nghĩa format lỗi & validation chung — _(sẽ tham chiếu theo project hiện có)._