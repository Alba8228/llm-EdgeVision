#!/bin/bash
# run.sh — YOLOv8 demo 启动器
# 设置 LD_LIBRARY_PATH 确保所有 .so 依赖从本地 lib/ 目录加载
# 用法: ./run.sh <demo_executable>   (参数已收敛到 ./llm.conf, 不再传命令行)
#   例如: ./run.sh ./rknn_yolov8_stream   /   ./run.sh ./rknn_udp_uart

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export LD_LIBRARY_PATH="${SCRIPT_DIR}/lib:${LD_LIBRARY_PATH:-}"

if [ $# -eq 0 ]; then
    echo "Usage: $0 <executable> [args...]"
    echo ""
    echo "Available executables:"
    for exe in "${SCRIPT_DIR}"/rknn_yolov8* "${SCRIPT_DIR}"/rknn_udp_uart; do
        [ -x "$exe" ] && echo "  $(basename "$exe")"
    done
    echo ""
    echo "Example:"
    echo "  $0 ./rknn_yolov8_stream    # 参数读自 ./llm.conf"
    echo "  $0 ./rknn_udp_uart         # 参数读自 ./llm.conf"
    exit 1
fi

exec "${SCRIPT_DIR}/$@"
