📑 SỔ GHI NỢ CHI TIÊU — ĐẶC TẢ TÍNH NĂNG
=========================================

> Tài liệu này mô tả **cái gì** hệ thống phải làm: tính năng, use case và quy tắc nghiệp vụ. Chi tiết **cách** hiện thực (schema, API, công nghệ, bảo mật) nằm trong `CLAUDE.md`, `AGENTS.md`, `.agents/rules/rules.md` và các tài liệu `/planning/*.md`. Phiên bản kỹ thuật chi tiết trước đây của tài liệu này (data model, endpoint, SQL) xem tại commit `6b19f01`.

1\. Tổng quan & bài toán
------------------------

Trong nhóm bạn bè / gia đình / đồng nghiệp, thường có một người đứng ra chi trả các khoản chung — bữa ăn, vé xe, phòng khách sạn — rồi chia lại cho mọi người. Ghi chép tay dễ sót, dễ nhầm, và khi có tranh cãi thì không có bằng chứng ai đã sửa gì.

Hệ thống là một **sổ ghi nợ chi tiêu cá nhân**: chỉ **chủ sổ** có tài khoản; các thành viên khác là đối tượng được ghi nợ, không cần tài khoản. Chủ sổ dùng hệ thống để:

*   Ghi lại từng khoản chi: **ai đứng ra trả**, **những ai hưởng** và **mỗi người gánh bao nhiêu**.
*   Phân loại chi tiêu (danh mục + nhãn) để xem thống kê, biểu đồ.
*   Gom các khoản chi theo **đợt** (chuyến đi, tháng, sự kiện), chốt đợt và tính ra **ai nợ ai bao nhiêu**.
*   Lưu vết mọi thay đổi trên số liệu để **tránh tranh cãi**.

**Kịch bản điển hình:** Nhóm 4 người đi du lịch 3 ngày. An (chủ sổ) tạo đợt "Đà Lạt 3/2026", mỗi lần chi tiêu An tạo một phiếu: "Ăn tối ngày 1" do Bình trả 800k, chia đều 4 người mỗi người gánh 200k; "Khách sạn" do An trả 2.000k, chia 4… Cuối chuyến An đóng đợt, hệ thống tính: Bình đã ứng 800k nhưng chỉ gánh 500k → được trả lại 300k; Cường không ứng gì, gánh 500k → nợ 500k. An xuất báo cáo kèm **ảnh QR chuyển khoản** gửi cả nhóm — ai nợ quét mã trả ngay; nếu ai thắc mắc "sao phần tôi lúc trước là 150k giờ thành 200k", An mở lịch sử thay đổi của phiếu để đối chiếu.

2\. Khái niệm
-------------

| Khái niệm | Mô tả |
|---|---|
| **Người dùng (chủ sổ)** | Chủ tài khoản. Toàn bộ dữ liệu bên dưới thuộc riêng một người dùng. |
| **Thành viên (member)** | Người tham gia chia tiền, do chủ sổ tự đặt tên quản lý; không có tài khoản. Mỗi sổ luôn có đúng một thành viên **đại diện chính chủ sổ**, sinh tự động khi đăng ký. |
| **Phiếu chi tiêu (expense)** | Một khoản chi: tên, mô tả, thời điểm chi, người trả, danh mục, nhãn, và danh sách phần gánh. Tổng tiền của phiếu = tổng các phần gánh. |
| **Người trả (payer)** | Thành viên đứng ra ứng toàn bộ tiền của phiếu. Mặc định là thành viên đại diện chủ sổ, có thể đổi. |
| **Phần gánh (share)** | Số tiền một thành viên phải chịu trong một phiếu, kèm ghi chú. Một phiếu có nhiều phần gánh. |
| **Danh mục (category)** | Phân loại bắt buộc, mỗi phiếu thuộc đúng một danh mục (Ăn uống, Đi lại…). Có màu/icon phục vụ biểu đồ. Mỗi sổ luôn có đúng một **danh mục mặc định**. |
| **Nhãn (tag)** | Phân loại tự do, tùy chọn, một phiếu gắn được nhiều nhãn. |
| **Đợt chi tiêu (event)** | Nhóm các phiếu theo một khoảng thời gian (chuyến đi, tháng…). Có trạng thái **đang mở / đã chốt**. |
| **Nhật ký thay đổi (audit log)** | Bằng chứng bất biến về mọi thay đổi số liệu: ai, lúc nào, hành động gì, giá trị trước/sau. |
| **Ví (wallet)** | Danh sách tài khoản ngân hàng (bank account) nhận tiền của chủ sổ, có đúng một tài khoản **mặc định**; là đích chuyển khoản khi tạo mã QR. |
| **Hạng người dùng** | Mỗi tài khoản thuộc hạng **Free** (tính năng cơ bản, có hạn mức) hoặc **Premium** (toàn bộ tính năng, không giới hạn). |

3\. Tính năng & Use case
------------------------

### 3.1. Tài khoản & phiên đăng nhập

*   Đăng ký tài khoản bằng username + mật khẩu. Khi đăng ký, hệ thống tự chuẩn bị sổ: tạo thành viên đại diện chủ sổ và bộ danh mục gợi ý (Ăn uống, Đi lại, Khách sạn, Mua sắm, Khác) trong đó một danh mục được đặt làm mặc định.
*   Đăng nhập / đăng xuất. Phiên đăng nhập có thời hạn và gia hạn được mà không cần nhập lại mật khẩu.
*   Đổi mật khẩu → mọi thiết bị đang đăng nhập đều bị buộc đăng nhập lại.

> **UC:** An cài app trên điện thoại mới → đăng nhập; nghi ngờ lộ mật khẩu → đổi mật khẩu, điện thoại cũ tự văng phiên.

### 3.2. Quản lý thành viên

*   Thêm, đổi tên thành viên.
*   **Xóa thành viên là xóa mềm:** thành viên biến mất khỏi các danh sách chọn khi tạo dữ liệu mới, nhưng **mọi số liệu lịch sử (phiếu, phần gánh, cân bằng nợ, thống kê) vẫn giữ nguyên và vẫn hiển thị tên người đó**. Danh sách thành viên có tùy chọn xem cả người đã xóa (phục vụ thống kê/export).

> **UC:** Cường rời nhóm; An xóa Cường. Các phiếu cũ có Cường vẫn nguyên vẹn, báo cáo đợt cũ vẫn tính Cường; nhưng khi tạo phiếu mới, Cường không còn trong danh sách chọn.

### 3.3. Danh mục chi tiêu

*   Thêm / sửa (tên, màu, icon) / xóa mềm danh mục. Tên danh mục không trùng nhau trong một sổ (tính trên các danh mục đang hoạt động).
*   Chọn một danh mục làm **mặc định**; thao tác này tự bỏ cờ mặc định của danh mục cũ. Không xóa được danh mục mặc định — luôn phải tồn tại đúng một.
*   Danh mục đã xóa không chọn được cho phiếu mới, nhưng các phiếu cũ vẫn giữ liên kết và vẫn thống kê được.

### 3.4. Nhãn

*   Thêm / đổi tên / xóa mềm nhãn. Tên nhãn không trùng trong một sổ (tính trên các nhãn đang hoạt động).
*   **Xóa nhãn là xóa mềm, giữ liên kết lịch sử** (đồng bộ với danh mục): nhãn biến mất khỏi danh sách chọn cho phiếu mới, nhưng các phiếu cũ vẫn giữ và hiển thị nhãn đó, vẫn lọc/thống kê được.
*   Tạo nhãn mới trùng tên với một nhãn đã xóa → **kích hoạt lại nhãn cũ** (liên kết lịch sử được nối liền, không sinh nhãn trùng tên).

### 3.5. Phiếu chi tiêu & phần gánh

*   Tạo phiếu gồm: tên, mô tả, thời điểm chi, người trả, danh mục, nhãn, danh sách phần gánh — **tạo trọn vẹn hoặc không tạo gì** (không bao giờ có phiếu "nửa vời" thiếu phần gánh).
    *   Không chọn người trả → mặc định là thành viên đại diện chủ sổ.
    *   Không chọn danh mục → dùng danh mục mặc định.
    *   Thành viên đại diện chủ sổ luôn xuất hiện trong danh sách phần gánh (mặc định 0 đồng nếu không nhập) — giữ chủ sổ luôn hiện diện trong màn chia tiền, dễ điều chỉnh về sau.
*   Sửa thông tin chung của phiếu (tên, mô tả, thời điểm, người trả, danh mục, tập nhãn).
*   Thêm / sửa / xóa từng phần gánh (số tiền, ghi chú, đổi thành viên) — tách bạch với việc sửa thông tin chung.
*   Xóa phiếu (kéo theo toàn bộ phần gánh của nó).
*   **Đánh dấu phiếu đã trả (settled):** ghi nhận một phiếu đã được tất toán — dùng chủ yếu cho **phiếu lẻ** (không thuộc đợt nào, nên không vào bảng cân bằng nợ) nhưng áp dụng được cho mọi phiếu nợ. Đây là **metadata thanh toán**, không phải số liệu chi tiêu: không làm thay đổi số tiền, và là ngoại lệ duy nhất được phép trên phiếu thuộc đợt đã chốt (đánh dấu sau khi mọi người chuyển khoản).
*   Xem danh sách phiếu, lọc theo đợt, khoảng thời gian, danh mục, nhãn, trạng thái đã trả / chưa trả.
*   **Export phiếu:** bảng tổng hợp mỗi thành viên gánh bao nhiêu trong phiếu, kèm ghi chú gộp. Định dạng **CSV** trước mắt; thiết kế export phải mở để bổ sung định dạng khác (Excel, JSON…) về sau.
*   **Xem lịch sử thay đổi của phiếu** (bao gồm cả các phần gánh của nó) — xem 3.8.

> **UC:** Bữa tối 800k Bình trả. An tạo phiếu, chọn Bình là người trả, nhập 4 phần gánh 200k. Hôm sau phát hiện Dung không ăn → An xóa phần gánh của Dung, sửa 3 phần còn lại thành ~267k. Mọi chỉnh sửa đều vào lịch sử.

### 3.6. Đợt chi tiêu

*   Tạo đợt với tên và khoảng thời gian; sửa thông tin đợt.
*   Gán phiếu vào đợt. **Thời điểm chi của phiếu phải nằm trong khoảng thời gian của đợt** — kiểm tra cả khi gán lẫn khi sửa thời điểm chi về sau. Phiếu không bắt buộc thuộc đợt nào.
*   **Gỡ phiếu khỏi đợt** và **xóa đợt** — chỉ được phép khi đợt **còn mở**. Xóa đợt không xóa phiếu: các phiếu bên trong trở thành phiếu lẻ.
*   **Chốt đợt (đóng):** đợt đã chốt là **chỉ đọc** — mọi thao tác ghi lên các phiếu trong đợt (sửa/xóa phiếu, thêm/sửa/xóa phần gánh, gán/gỡ phiếu) đều bị từ chối với thông báo rõ ràng (ngoại lệ duy nhất: đánh dấu đã trả — 3.5). Chỉ còn xem và export. **Chốt là một chiều: không mở lại được.** Hệ thống **không bao giờ tự chốt đợt** — kể cả khi qua ngày kết thúc, chủ sổ phải chủ động chốt.
*   **Export đợt:** bảng tổng hợp phần gánh của từng thành viên trên toàn đợt + **bảng cân bằng nợ** (xem 3.7). Định dạng CSV trước mắt (như export phiếu). Sau khi chốt còn tạo được **ảnh QR chuyển khoản** cho cả đợt (xem 3.10).

> **UC:** Kết thúc chuyến Đà Lạt, An bấm "Chốt đợt". Một tuần sau Bình đòi sửa phiếu ăn tối → hệ thống từ chối: đợt đã chốt, số liệu đã đông cứng làm căn cứ đối chiếu.

### 3.7. Cân bằng nợ (tính năng lõi)

Với mỗi phiếu, người trả **ứng trước toàn bộ** tổng tiền phiếu; mỗi thành viên chịu đúng phần gánh của mình.

**Phạm vi tính: cân bằng nợ chỉ có nghĩa trong một đợt.** Phiếu lẻ (không thuộc đợt nào) không tham gia bảng cân bằng — công nợ của chúng theo dõi bằng **đánh dấu đã trả** trên từng phiếu (3.5). Trong một đợt:

*   `đã ứng` = tổng tiền các phiếu mà thành viên đó là người trả.
*   `phải gánh` = tổng các phần gánh của thành viên đó.
*   `cân bằng = đã ứng − phải gánh`. **Dương → người khác đang nợ thành viên này; Âm → thành viên này đang nợ.**

Tổng cân bằng của mọi thành viên trong một phạm vi luôn bằng 0.

> **UC:** Trong đợt, Bình ứng 800k, gánh 500k → +300k (được nhận lại). Cường ứng 0, gánh 500k → −500k (phải trả). Báo cáo chốt đợt liệt kê rõ từng người.

### 3.8. Nhật ký thay đổi (audit)

*   Phạm vi: **phiếu chi tiêu và phần gánh** — mọi lần tạo / sửa / xóa.
*   Mỗi bản ghi nhật ký gồm: ai thao tác, lúc nào, hành động gì, dữ liệu trước và sau thay đổi. Sửa mà không đổi gì thì không sinh log rác.
*   Nhật ký **bất biến**: không sửa, không xóa được, và vẫn tồn tại kể cả khi phiếu/phần gánh gốc đã bị xóa. Nhật ký chung số phận với thao tác gốc — thao tác thất bại thì không có log.
*   Xem lịch sử theo từng phiếu, sắp theo thời gian.

### 3.9. Thống kê

*   **Tổng quan:** tổng chi tiêu trong một khoảng thời gian. Cân bằng nợ của thành viên xem theo **từng đợt** (3.7), không tính gộp xuyên đợt.
*   **Theo danh mục:** tổng chi và số phiếu của từng danh mục (kèm màu) trong khoảng thời gian hoặc trong một đợt — phục vụ biểu đồ tròn/cột.

### 3.10. Ví & QR chuyển khoản

*   **Ví (wallet):** chủ sổ quản lý danh sách **tài khoản ngân hàng** của mình — thêm / sửa / xóa, đặt đúng một tài khoản làm **mặc định**. Tài khoản mặc định là đích nhận tiền khi tạo mã QR (có thể chọn tài khoản khác lúc tạo).
*   **QR cho phiếu:** chủ sổ **chủ động tạo** khi cần, mỗi lần tạo ra **một mã QR đại diện cho cả phiếu** với số tiền = tổng tiền phiếu — thành viên quét là chuyển khoản nhanh, khỏi gõ tay số tiền/số tài khoản.
*   **QR cho đợt:** chỉ khả dụng **sau khi đợt đã chốt** (số liệu đã đông cứng). Hệ thống tạo **một mã QR cho mỗi thành viên còn nợ** (cân bằng âm), số tiền = đúng số nợ của người đó, rồi gom toàn bộ thành **một ảnh duy nhất** (kèm tên + số tiền từng người) để chia sẻ cho cả nhóm.

> **UC:** Chốt đợt Đà Lạt xong, An bấm "Tạo QR đợt" → nhận một ảnh gom các mã QR kèm tên và số tiền từng người, gửi vào nhóm chat. Cường quét mã của mình — app ngân hàng điền sẵn 500k và tài khoản mặc định của An; chuyển xong An đánh dấu đã trả.

### 3.11. Hạng người dùng (Premium / Free)

*   Mỗi tài khoản thuộc một hạng: **Free** (mặc định khi đăng ký) hoặc **Premium**.
*   **Free** dùng các tính năng cơ bản với **hạn mức sử dụng**. **Premium** dùng toàn bộ tính năng — kể cả nhóm mở rộng — và **không giới hạn** hạn mức.
*   **Nâng hạng bằng thanh toán.** Chi tiết (giá, chu kỳ, cổng thanh toán, gia hạn/hết hạn) là **thiết kế mở** — chốt trong planning doc của tính năng này; đặc tả chỉ cố định nguyên tắc: Free là mặc định, trả tiền để lên Premium.
*   Phân nhóm đề xuất *(con số hạn mức cuối cùng chốt trong planning doc — xem mục 5)*:
    *   **Cơ bản (Free):** thành viên, danh mục, nhãn, phiếu & phần gánh, đánh dấu đã trả, đợt, cân bằng nợ, thống kê, audit, export CSV — kèm hạn mức (ví dụ: tối đa N thành viên, M đợt đang mở, K phiếu/tháng).
    *   **Mở rộng (chỉ Premium):** ví & tạo QR chuyển khoản, các định dạng export bổ sung (Excel/JSON), và các tính năng tương lai (chia sẻ đợt, nhắc nợ, đa tiền tệ).
*   Chạm hạn mức → từ chối **tạo mới** với thông báo rõ ràng. **Không bao giờ** khóa/ẩn/xóa dữ liệu đã có — kể cả khi hạ hạng, dữ liệu vượt hạn mức vẫn đọc/sửa bình thường, chỉ chặn tạo thêm.

4\. Quy tắc nghiệp vụ (bắt buộc)
--------------------------------

1.  **Riêng tư tuyệt đối:** mỗi người dùng chỉ nhìn thấy và thao tác được dữ liệu của chính mình. Truy cập dữ liệu không thuộc về mình phải trông y hệt như dữ liệu **không tồn tại** (không được để lộ là "có nhưng không có quyền").
2.  **Toàn vẹn liên kết trong một sổ:** người trả của phiếu, thành viên của phần gánh, danh mục và nhãn gắn vào phiếu — tất cả phải thuộc cùng một chủ sổ với phiếu.
3.  **Tiền bạc chính xác:** số tiền không âm; không chấp nhận sai số làm tròn kiểu số thực.
4.  **Đợt đã chốt là bất biến:** không thao tác ghi nào lên phiếu trong đợt được phép; chỉ đọc/export/tạo QR. Chốt không đảo ngược được. Ngoại lệ duy nhất: **đánh dấu đã trả** (3.5) — metadata thanh toán, không phải số liệu chi tiêu.
5.  **Nguyên tử:** tạo/xóa phiếu cùng các phần gánh của nó phải trọn vẹn — thành công toàn bộ hoặc không có gì thay đổi.
6.  **Danh mục mặc định luôn tồn tại đúng một** cho mỗi sổ; không xóa được; phiếu vì thế luôn có danh mục hợp lệ.
7.  **Xóa mềm, lịch sử bất khả xâm phạm:** xóa thành viên/danh mục/nhãn không được làm thay đổi hay ẩn số liệu lịch sử.
8.  **Không chọn tài nguyên đã xóa cho dữ liệu mới:** danh mục, nhãn hay thành viên đã xóa không gán được cho phiếu / phần gánh **mới**; nhưng dữ liệu cũ vẫn giữ liên kết và hiển thị đầy đủ thông tin của họ.
9.  **Hạn mức theo hạng chỉ chặn tạo mới:** vượt hạn mức (Free) → từ chối tạo thêm với thông báo rõ ràng; không bao giờ ảnh hưởng dữ liệu hiện có.

5\. Các quyết định đã chốt & chi tiết để mở có chủ đích
--------------------------------------------------------

**Đã chốt 2026-07-10** (đưa thẳng vào các mục trên):

*   Đợt: không mở lại sau khi chốt; xóa đợt / gỡ phiếu chỉ khi còn mở; hệ thống không bao giờ tự chốt.
*   Cân bằng nợ chỉ trong phạm vi đợt; phiếu lẻ và mọi phiếu nợ dùng **đánh dấu đã trả**.
*   Thành viên đã xóa: không dùng cho dữ liệu mới, phiếu cũ hiển thị đầy đủ.
*   Export: CSV trước mắt, thiết kế mở cho định dạng khác.
*   Giữ phần gánh 0 đồng của chủ sổ trong mọi phiếu.
*   Nhãn đã xóa: **giữ liên kết lịch sử như danh mục**; tạo lại trùng tên → kích hoạt lại nhãn cũ.
*   QR: với **phiếu** — tạo thủ công, một mã đại diện cả phiếu (số tiền = tổng phiếu); với **đợt** — sau khi chốt, một mã mỗi thành viên còn nợ, gom một ảnh.
*   Hạng người dùng: nâng từ Free lên Premium **bằng thanh toán**.
*   Thuật ngữ tiếng Anh (dùng cho entity/bảng/endpoint về sau) chốt 2026-07-10: phiếu = **expense**, phần gánh = **share**, đợt = **event**, ví/tài khoản = **wallet/bank account**, đã trả = **settled**, hạng = **Premium/Free** ("voucher/record/batch/Primary-Regular" bị loại vì lệch nghĩa).

**Chi tiết để mở có chủ đích** (không chặn đặc tả — chốt trong planning doc của tính năng tương ứng):

*   Con số hạn mức cụ thể của Free (số thành viên / đợt đang mở / phiếu mỗi tháng) và danh sách cuối cùng của nhóm "mở rộng".
*   Cơ chế thanh toán nâng hạng: giá, chu kỳ, cổng thanh toán, xử lý hết hạn.

6\. Tính năng tương lai
-----------------------

*   **Chia sẻ đợt (chỉ xem):** link công khai có bảo vệ để thành viên xem báo cáo đợt mà không cần tài khoản.
*   **Đa tiền tệ:** lưu tỷ giá tại thời điểm tạo phiếu.
*   **Nhắc nợ:** tự động gửi email/Zalo/Telegram cho thành viên còn nợ khi đợt được chốt.
*   **Audit mở rộng:** phạm vi sang thành viên/đợt/danh mục; diff theo từng trường; khôi phục bản ghi từ snapshot.
*   **Đánh dấu đã trả theo từng thành viên:** chi tiết hơn mức phiếu — theo dõi từng người đã trả phần của mình hay chưa.
