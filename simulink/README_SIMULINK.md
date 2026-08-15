# Hướng dẫn Thiết lập & Xây dựng Mô phỏng Simulink với vECU

Tài liệu này hướng dẫn bạn cách thiết lập thư mục và từng bước kéo khối xây dựng mô hình nhiệt động học động cơ trong MATLAB/Simulink để nhúng, cấp dữ liệu thời gian thực cho `vECU`.

---

## 1. Cấu trúc Thư mục Thiết lập

Thư mục dành cho mô phỏng Simulink được đặt tại:
`c:\Users\Admin\Downloads\vECU_Automated_Testing_Framework\simulink`

Thư mục bao gồm:
* [send_to_vecu.m](file:///c:/Users/Admin/Downloads/vECU_Automated_Testing_Framework/simulink/send_to_vecu.m): Tệp hàm MATLAB chịu trách nhiệm kết nối TCP Socket và gửi dữ liệu cảm biến đóng gói chuẩn DoIP/UDS sang `vECU.exe`.
* `vECU_Engine_Simulation.slx` *(Tệp mô hình Simulink bạn sẽ tạo theo hướng dẫn bên dưới)*.

---

## 2. Hướng dẫn Từng bước xây dựng mô hình Simulink

### Bước 1: Khởi tạo mô hình
1. Mở phần mềm **MATLAB**.
2. Nhập lệnh `simulink` trong cửa sổ Command Window của MATLAB để mở Simulink Start Page.
3. Chọn **Blank Model** để tạo mô hình trống mới.
4. Lưu mô hình này với tên: `vECU_Engine_Simulation.slx` trong thư mục `simulink` trên.

### Bước 2: Kéo các khối tạo Động học Xe (RPM & Vận tốc)
Mở thư viện Library Browser của Simulink và kéo các khối sau vào sơ đồ:
1. **Khối Ngõ vào (Thanh trượt ga)**:
   * Tìm khối **Slider Gain** hoặc khối **Constant** để đại diện cho độ mở bàn đạp ga (giá trị từ `0` đến `1`). Đặt tên khối này là `Throttle (0 to 1)`.
2. **Khối giả lập Vòng tua máy (Engine RPM)**:
   * Kéo khối **Gain** (đặt giá trị là `3000`).
   * Kéo khối **Bias** (đặt giá trị là `800` - đại diện cho vòng tua không tải Idle RPM).
   * Kéo khối **Transfer Fcn** (khối hàm truyền để tạo độ trễ/mượt vật lý khi tăng ga, cấu hình mẫu số là `[0.5 1]`).
   * *Nối dây*: `Throttle` ➔ `Gain` ➔ `Transfer Fcn` ➔ `Bias` ➔ Đầu ra là **RPM**.
3. **Khối giả lập Vận tốc xe (Vehicle Speed)**:
   * Kéo khối **Gain** (đặt giá trị là hệ số quy đổi tỉ số truyền và bánh xe, ví dụ: `0.03`).
   * *Nối dây*: Nối từ ngõ ra **RPM** ➔ Khối **Gain** này ➔ Đầu ra là **Vehicle Speed (km/h)**.

### Bước 3: Kéo các khối tạo Nhiệt độ Nước làm mát (Coolant Temp)
Hệ thống làm mát sẽ nóng lên theo Vòng tua máy (RPM) và tản nhiệt theo chênh lệch nhiệt độ môi trường:
1. Kéo khối **Integrator** để tính tích lũy nhiệt độ. Đặt giá trị ban đầu (Initial condition) là `90` (90°C).
2. Kéo khối **Gain** (hệ số sinh nhiệt, đặt `0.005`) nối từ ngõ ra **RPM**.
3. Kéo khối **Gain** (hệ số tản nhiệt tự nhiên, đặt `0.02`).
4. Kéo khối **Sum** (khối cộng trừ) để tính toán biến thiên nhiệt độ:
   * $\text{Nhiệt sinh ra due to RPM} - \text{Nhiệt tản ra do gió}$.
5. **Giả lập Sự cố (Coolant Leakage - Tiêm lỗi)**:
   * Kéo khối **Switch** và một khối **Constant** mang giá trị `130` (đại diện cho nhiệt độ cực đại khi rò rỉ két nước).
   * Kéo khối **Manual Switch** nối từ một nút gạt sang khối Constant `130` này để chủ động kích hoạt quá nhiệt.
   * Đầu ra của khối Switch này chính là **Coolant Temperature**.

### Bước 4: Khối Giao tiếp mạng (TCP/IP Send) gửi sang vECU
1. Kéo khối **MATLAB Function** trong thư viện *User-Defined Functions* vào sơ đồ.
2. Nhấp đúp vào khối này để mở trình chỉnh sửa code và dán nội dung sau:
   ```matlab
   function y = fcn(speed, rpm, temp)
       % Gọi hàm gửi dữ liệu sang vECU qua DoIP
       send_to_vecu(speed, rpm, temp);
       y = 0; % output giả định
   end
   ```
3. *Nối dây*: 
   * Cổng vào `speed` ➔ nối với tín hiệu **Vehicle Speed**.
   * Cổng vào `rpm` ➔ nối với tín hiệu **Engine RPM**.
   * Cổng vào `temp` ➔ nối với tín hiệu **Coolant Temperature**.
4. Kéo khối **Terminator** nối vào ngõ ra `y` của khối MATLAB Function để dọn dẹp sơ đồ.

---

## 3. Cách Vận hành Đồng mô phỏng (Co-Simulation)

1. Mở PowerShell hoặc Command Prompt tại thư mục dự án và khởi chạy máy ảo `vECU`:
   ```powershell
   c:\Users\Admin\Downloads\vECU_Automated_Testing_Framework\apps\vecu\build\vECU.exe
   ```
2. Trên **Simulink**, cấu hình thời gian chạy mô phỏng là `inf` (vô hạn) và nhấn nút **Run** (Chạy mô phỏng).
3. Mở phần mềm **Test Runner Desktop UI (C#)** hoặc chạy các tệp kịch bản kiểm thử chẩn đoán để quan sát kết quả:
   * Bạn có thể kéo thanh trượt `Throttle` trong Simulink từ `0` lên `1` để thấy kim Vòng tua và Vận tốc trên giao diện Test UI thay đổi thời gian thực.
   * Gạt công tắc `Manual Switch` giả lập rò rỉ két nước làm mát ➔ Nhiệt độ nhảy lên `130°C` ➔ Đèn cảnh báo Check Engine sáng đỏ và mã lỗi `P0115` xuất hiện trên Client!
