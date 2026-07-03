"""
async_benchmark.py
=============================================================================
Performance Benchmark Tool — UAV Monitor Ingestion Service (Async Burst Edition)
=============================================================================
Xả tải theo dạng BURST (từng đợt lớn đồng loạt), hoàn toàn Fire-and-Forget.
"""

import argparse
import asyncio
import os
import random
import time
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
import numpy as np
import aiohttp


# =============================================================================
#  NẠP BIẾN MÔI TRƯỜNG .ENV
# =============================================================================

def _load_env(env_path: str = ".env") -> dict:
    env = {}
    path = Path(env_path)
    if not path.exists():
        return env
    for line in path.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        env[key.strip()] = value.strip().strip('"').strip("'")
    return env


_env = _load_env(".env")
API_URL = os.environ.get("API_URL", _env.get("API_URL", "http://localhost:80"))
API_KEY = os.environ.get("API_KEY", _env.get("API_KEY", "YOUR_KEY"))
DEVICE_ID = int(os.environ.get("DEVICE_ID", _env.get("DEVICE_ID", "1004")))

REQUEST_TIMEOUT = 5

# =============================================================================
#  DỮ LIỆU GIẢ LẬP UAV
# =============================================================================

DRONE_TYPES = {
    "DRONE": "DJI Mavic 3",
    "DRONE_SIGNAL": "RF Transmission",
    "NO_DRONE": "None",
}


def build_payload(device_id: int) -> dict:
    pred_class = random.choice(["DRONE", "DRONE_SIGNAL", "NO_DRONE"])
    is_drone = pred_class != "NO_DRONE"
    return {
        "deviceId": device_id,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "status": "Online",
        "detected": 1 if is_drone else 0,
        "droneType": DRONE_TYPES[pred_class],
        "accuracy": round(random.uniform(0.75, 0.99), 4),
        "controlState": "Active" if is_drone else "None",
        "latency": round(random.uniform(10.0, 25.0), 1),
        "frequency": float(random.choice([2412, 2437, 2462]) if is_drone else 0),
    }


# =============================================================================
#  THỐNG KÊ ĐỊNH KỲ THEO CỬA SỔ WINDOWS
# =============================================================================

class AsyncWindowMetricsCollector:
    def __init__(self):
        self.reset()

    def reset(self):
        self.latencies = []
        self.status_codes = defaultdict(int)
        self.total = 0
        self.success = 0
        self.start_time = time.monotonic()

    def record(self, success: bool, status_code: int, latency_ms: float):
        self.total += 1
        self.status_codes[status_code] += 1
        if success:
            self.success += 1
            self.latencies.append(latency_ms)

    def print_snapshot(self):
        elapsed_s = time.monotonic() - self.start_time
        lats = sorted(self.latencies)
        n = len(lats)

        def pct(p):
            if not lats: return 0.0
            idx = max(0, int(n * p / 100) - 1)
            return lats[idx]

        throughput = self.total / max(elapsed_s, 0.001)
        success_rate = (100 * self.success / max(self.total, 1))
        code_str = "  ".join(f"HTTP {k}: {v}" for k, v in sorted(self.status_codes.items()))

        timestamp = datetime.now().strftime("%H:%M:%S")
        print(f"[{timestamp}] 🔥 BURST REPORT | Actual RPS: {round(throughput, 1)} | Success: {success_rate:.2f}% | Total window: {self.total}")
        if lats:
            print(f"    ↳ Latency: p50: {pct(50):.1f}ms | p95: {pct(95):.1f}ms | p99: {pct(99):.1f}ms | mean: {np.mean(lats):.1f}ms")
        print(f"    ↳ Status : {code_str if code_str else 'None'}\n" + "-"*50)


# =============================================================================
#  LUỒNG XẢ ĐẠN FIRE-AND-FORGET
# =============================================================================

async def send_request_task(session: aiohttp.ClientSession, url: str, api_key: str, device_id: int,
                            collector: AsyncWindowMetricsCollector):
    payload = build_payload(device_id)
    headers = {
        "Content-Type": "application/json",
        "X-Device-API-Key": str(api_key).strip(),
        "User-Agent": "UAV-Async-Burst-Benchmark/4.0",
    }

    t0 = time.monotonic()
    try:
        # Bắn request đi, khi có response về sẽ tự ghi nhận vào collector mà không block vòng lặp chính
        async with session.post(url, json=payload, headers=headers, timeout=REQUEST_TIMEOUT) as response:
            await response.read()
            latency = (time.monotonic() - t0) * 1000
            collector.record(response.status in [200, 202], response.status, latency)
    except Exception:
        latency = (time.monotonic() - t0) * 1000
        collector.record(False, 0, latency)


# =============================================================================
#  TASK IN BÁO CÁO TỰ ĐỘNG
# =============================================================================

async def report_loop(collector: AsyncWindowMetricsCollector, interval: int = 5):
    while True:
        await asyncio.sleep(interval)
        collector.print_snapshot()
        collector.reset()


# =============================================================================
#  BỘ ĐIỀU PHỐI CHÍNH
# =============================================================================

async def main_async():
    parser = argparse.ArgumentParser(description="UAV Monitor Async Burst Infinite Benchmark")
    parser.add_argument("--url", default=API_URL, help="URL của Ingestion Service Server")
    parser.add_argument("--api-key", default=API_KEY, help="X-Device-API-Key để bypass BCrypt")
    parser.add_argument("--device-id", default=DEVICE_ID, type=int, help="Device ID của UAV phát tín hiệu")
    parser.add_argument("--burst-size", default=5000, type=int, help="Số lượng request xả đồng loạt trong mỗi đợt")
    parser.add_argument("--burst-interval", default=1.0, type=float, help="Thời gian nghỉ giữa các đợt burst (giây)")
    parser.add_argument("--report-interval", default=5, type=int, help="Thời gian giãn cách giữa các lần in báo cáo (giây)")
    args = parser.parse_args()

    endpoint = args.url if args.url.endswith("/api/v1/telemetry/log") else args.url.rstrip("/") + "/api/v1/telemetry/log"

    print("=" * 60)
    print("  🚀 UAV Monitor — Async BURST Extreme Benchmark 🚀")
    print("=" * 60)
    print(f"  🎯 Target URL    : {endpoint}")
    print(f"  🔑 API Key       : {args.api_key[:10]}...")
    print(f"  💥 Burst Size    : {args.burst_size} reqs / đợt")
    print(f"  ⏱️  Burst Interval: {args.burst_interval} giây")
    print(f"  📊 Report Every  : {args.report_interval} giây")
    print("=" * 60 + "\n🔥 Launching endless burst attacks... Press Ctrl+C to stop.\n")

    collector = AsyncWindowMetricsCollector()
    background_tasks = set()

    # Khởi chạy bộ in báo cáo định kỳ
    asyncio.create_task(report_loop(collector, args.report_interval))

    # Cấu hình Connection Pool cực đại để tránh bị nghẽn cổ chai ngay tại client khi nổ burst
    connector = aiohttp.TCPConnector(
        limit=args.burst_size * 3,
        limit_per_host=args.burst_size * 3,
        ttl_dns_cache=300
    )

    async with aiohttp.ClientSession(connector=connector) as session:
        try:
            while True:
                t_start_burst = time.monotonic()

                # 💥 XẢĐẠN ĐỒNG LOẠT: Khởi tạo cực nhanh N task cùng một lúc
                for _ in range(args.burst_size):
                    task = asyncio.create_task(
                        send_request_task(session, endpoint, args.api_key, args.device_id, collector)
                    )
                    background_tasks.add(task)
                    task.add_done_callback(background_tasks.discard)

                # Tính toán thời gian thực tế đã mất để spawn đợt burst nhằm trừ hao chính xác
                elapsed_spawning = time.monotonic() - t_start_burst
                sleep_time = max(0.001, args.burst_interval - elapsed_spawning)

                # Hoàn toàn không đợi request xong, ngủ đúng thời gian giãn cách rồi nổ đợt tiếp theo
                await asyncio.sleep(sleep_time)

                # Quản lý bộ nhớ tránh pending tasks phình to nếu server sập hẳn
                if len(background_tasks) > args.burst_size * 10:
                    print(f"[Warning] Quá nhiều task chưa phản hồi ({len(background_tasks)}). Đang đợi server hạ tải bớt...")
                    await asyncio.sleep(1.0)

        except asyncio.CancelledError:
            print("\nStopping burst benchmark...")
        finally:
            if background_tasks:
                print(f"Cleaning up {len(background_tasks)} remaining tasks...")
                await asyncio.gather(*background_tasks, return_exceptions=True)
            print("Stopped clean.")


if __name__ == "__main__":
    try:
        asyncio.run(main_async())
    except KeyboardInterrupt:
        pass