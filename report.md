# Homework: Tăng tải trên Service nhận telemetry

## 1. Mục tiêu

Hiện tại service nhận telemetry của em xử lí được khoảng 3000 request/s và hệ thống sẽ chạy chậm hơn khi cho lên 10000 request/s. Do đó yêu cầu là phải tối ưu service nhận telemetry của em sao cho có thể xử lí được trên 20000 request/s.

Phạm vi: Homework của em chỉ tối ưu phần service nhận telemetry để đạt hiệu suất tối đa, không động tới scale thêm nhiều instance của service nhận telemetry không được tính. Hoạt động tăng tải chỉ diễn ra trong folder **IngestionService**.

## 2. Điểm yếu của code cũ (anh có thể check lại trong branch kafka):

![alt text](img_homework/flow_old.png)

- Thiết bị gửi telemetry lên endpoint `/api/v1/telemetry/log`.
- IngestionService nhận request, kiểm tra deviceID tạo thành payload và gửi xuống Kafka producer với topic `uav.telemetry.raw` rồi mới gửi response 202 -> Mục đích để đảm bảo telemetry được gửi xuống kafka, tuy nhiên lại gây ra độ trễ cho request.
- Worker phía dưới consume theo batch để lưu vô Clickhouse, đồng thời update latest log hoặc trạng thái thiết bị trên Redis.

## 3. Thiết kế mới

### 3.1 Flow tổng quan

![alt text](img_homework/flow_new.png)

### 3.2 Controller khong publish Kafka truc tiep nua

Controller mới không gọi Kafka producer trực tiếp. Thay vào đó, controller chi dua telemetry vao `TelemetryIngestionQueue` (được lưu trong RAM) sau do tra `202` ngay cho client, tránh bị phụ thuộc vào Kafka.

### 3.3 Producer Consumer

`ProducerWorker` là background service chạy vòng lặp nhiều workers song song giúp đọc từ `TelemetryIngestionQueue` và đây message sang Kafka. Áp dụng kĩ thuật multi-thread để tăng tốc độ xử lý telemetry để tạo ra nhiều worker chạy song song thực thụ trên nhiều nhân CPU biệt lập

```
        var workers = Enumerable.Range(0, _workerCount)
            .Select(workerId => RunProducerLoopAsync(workerId, stoppingToken));
        await Task.WhenAll(workers);
```

Tác vụ nặng nhất tại tầng này là tuần tự hóa đối tượng sang chuỗi văn bản (JsonSerializer.Serialize). Nhờ có 8 luồng chạy song song, chi phí tính toán văn bản này được chia nhỏ ra đa nhân.

```
                // Đóng gói packet thành JSON string
                var jsonPacket = JsonSerializer.Serialize(packet);

                // Đẩy vô producer, tách biệt thao tác của Kafka khỏi luồng HTTP
                _producer.Produce(TopicName, new Message<string, string>
                {
                    Key = packet.DeviceId.ToString(),
                    Value = jsonPacket
                });
```

ConsumerWorker liên tục rút dữ liệu từ Kafka Topic về khay RAM theo mẻ cực lớn với cấu hình BatchSize = 20000 bản tin hoặc kích hoạt ngắt thời gian chờ sau mỗi BatchTimeoutMs = 1000 (1 giây).

## 3. Tiến hành benchmark

### 3.1 Yêu cầu:

- Như đã bàn ở cuộc phỏng vấn, em sẽ cố để đẩy lượng tps lên cao nhất có thể tới khi CPU đã chạm ngưỡng
- Em sẽ benchmark trên một máy duy nhất có hệ thống em đang chạy
- Qua quá trình thử nhiều lần, em nhận ra dùng code python sẽ không thể nào đáp ứng được việc gửi các burst 10-20k request/s. Do đó em quyết định tìm kiếm tool cho benchmark và em chọn Vegeta.exe:
  https://github.com/tsenart/vegeta
  https://sourceforge.net/projects/vegeta.mirror/

## 3.2 Benchmark kết quả:

Máy em sử dụng có cấu hình i7 CPU 16Core 16GB RAM
