#!/bin/bash

# ==================== 配置项 ====================
REFRESH_INTERVAL=1          # 刷新间隔（秒）
NPU_DEVFREQ_PATH="/sys/class/devfreq/fdab0000.npu"
DDR_DEVFREQ_PATH="/sys/class/devfreq/dmc"
GPU_DEVFREQ_PATH="/sys/class/devfreq/fb000000.gpu"
CPU_CORE_NUM=8              # RK3588 共 8 个 CPU 核心
RGA_LOAD_FILE="/sys/kernel/debug/rkrga/load"  # RGA 负载节点
# ================================================

# 每个CPU核心的历史统计缓存
declare -a prev_cpu_total
declare -a prev_cpu_idle
declare -a cpu_usage

# 核心类型标注
core_type=(
  "A55 小核"
  "A55 小核"
  "A55 小核"
  "A55 小核"
  "A76 大核"
  "A76 大核"
  "A76 超大核"
  "A76 超大核"
)

# 捕获 Ctrl+C 优雅退出
trap 'echo -e "\n监控已退出"; exit 0' INT

# 初始化：首次CPU采样保存基准值
init_cpu_stats() {
  for i in $(seq 0 $((CPU_CORE_NUM - 1))); do
    local stat_line=$(grep "^cpu${i} " /proc/stat)
    local fields=($stat_line)
    local user=${fields[1]}
    local nice=${fields[2]}
    local system=${fields[3]}
    local idle=${fields[4]}
    local iowait=${fields[5]}
    local irq=${fields[6]}
    local softirq=${fields[7]}
    local steal=${fields[8]:-0}

    local total=$((user + nice + system + idle + iowait + irq + softirq + steal))
    local total_idle=$((idle + iowait))

    prev_cpu_total[$i]=$total
    prev_cpu_idle[$i]=$total_idle
  done
}

# 计算每个 CPU 核心的独立使用率
calc_per_cpu_usage() {
  for i in $(seq 0 $((CPU_CORE_NUM - 1))); do
    local stat_line=$(grep "^cpu${i} " /proc/stat)
    local fields=($stat_line)
    local user=${fields[1]}
    local nice=${fields[2]}
    local system=${fields[3]}
    local idle=${fields[4]}
    local iowait=${fields[5]}
    local irq=${fields[6]}
    local softirq=${fields[7]}
    local steal=${fields[8]:-0}

    local total=$((user + nice + system + idle + iowait + irq + softirq + steal))
    local total_idle=$((idle + iowait))

    local delta_total=$((total - prev_cpu_total[i]))
    local delta_idle=$((total_idle - prev_cpu_idle[i]))

    if [ "$delta_total" -eq 0 ]; then
      cpu_usage[$i]=0
    else
      cpu_usage[$i]=$(( (delta_total - delta_idle) * 100 / delta_total ))
    fi

    prev_cpu_total[$i]=$total
    prev_cpu_idle[$i]=$total_idle
  done
}

# 获取 NPU 三个核心各自的使用率
get_npu_per_core_usage() {
  local npu_load_file="/sys/kernel/debug/rknpu/load"
  
  if [ ! -f "$npu_load_file" ]; then
    npu_core0="N/A"
    npu_core1="N/A"
    npu_core2="N/A"
    return
  fi

  local raw=$(cat "$npu_load_file" 2>/dev/null)
  if [ -z "$raw" ]; then
    npu_core0="N/A"
    npu_core1="N/A"
    npu_core2="N/A"
    return
  fi

  # 解析三个核心的百分比数值
  npu_core0=$(echo "$raw" | grep -oP 'Core0:\s*\K\d+' || echo "N/A")
  npu_core1=$(echo "$raw" | grep -oP 'Core1:\s*\K\d+' || echo "N/A")
  npu_core2=$(echo "$raw" | grep -oP 'Core2:\s*\K\d+' || echo "N/A")
}

# 获取各硬件实时频率
get_freq_info() {
  local freq

  # CPU 各簇频率（同簇同频）
  freq=$(cat /sys/devices/system/cpu/cpufreq/policy0/scaling_cur_freq 2>/dev/null)
  cpu0_freq_mhz=$([ -n "$freq" ] && echo $((freq / 1000)) || echo "N/A")

  freq=$(cat /sys/devices/system/cpu/cpufreq/policy4/scaling_cur_freq 2>/dev/null)
  cpu4_freq_mhz=$([ -n "$freq" ] && echo $((freq / 1000)) || echo "N/A")

  freq=$(cat /sys/devices/system/cpu/cpufreq/policy6/scaling_cur_freq 2>/dev/null)
  cpu6_freq_mhz=$([ -n "$freq" ] && echo $((freq / 1000)) || echo "N/A")

  # NPU 频率（三核共用同一频率）
  freq=$(cat "$NPU_DEVFREQ_PATH/cur_freq" 2>/dev/null)
  npu_freq_mhz=$([ -n "$freq" ] && echo $((freq / 1000000)) || echo "N/A")

  # DDR 频率
  freq=$(cat "$DDR_DEVFREQ_PATH/cur_freq" 2>/dev/null)
  ddr_freq_mhz=$([ -n "$freq" ] && echo $((freq / 1000000)) || echo "N/A")
}

# 获取内存使用信息
get_mem_info() {
  local mem_total_kb=$(grep MemTotal /proc/meminfo | awk '{print $2}')
  local mem_avail_kb=$(grep MemAvailable /proc/meminfo | awk '{print $2}')
  local mem_used_kb=$((mem_total_kb - mem_avail_kb))

  mem_total_mb=$((mem_total_kb / 1024))
  mem_used_mb=$((mem_used_kb / 1024))
  mem_usage_pct=$(awk "BEGIN {printf \"%.1f\", $mem_used_kb * 100 / $mem_total_kb}")
}

# RGA 负载获取
get_rga_usage() {
  if [ ! -f "$RGA_LOAD_FILE" ]; then
    rga3_0="N/A"
    rga3_1="N/A"
    rga2_0="N/A"
    return
  fi

  local raw=$(cat "$RGA_LOAD_FILE" 2>/dev/null)
  if [ -z "$raw" ]; then
    rga3_0="N/A"
    rga3_1="N/A"
    rga2_0="N/A"
    return
  fi

  # 按顺序提取3个调度器负载：前2个为RGA3核，第3个为RGA2核
  rga3_0=$(echo "$raw" | grep 'load =' | sed -n '1p' | awk -F'= ' '{print $2}' | tr -d ' %' || echo "N/A")
  rga3_1=$(echo "$raw" | grep 'load =' | sed -n '2p' | awk -F'= ' '{print $2}' | tr -d ' %' || echo "N/A")
  rga2_0=$(echo "$raw" | grep 'load =' | sed -n '3p' | awk -F'= ' '{print $2}' | tr -d ' %' || echo "N/A")
}

# 新增：GPU 使用率与频率获取
get_gpu_info() {
  # GPU 负载（格式通常为 load@freq，提取@前数值）
  if [ -f "$GPU_DEVFREQ_PATH/load" ]; then
    local gpu_load_raw=$(cat "$GPU_DEVFREQ_PATH/load" 2>/dev/null)
    gpu_usage=$(echo "$gpu_load_raw" | cut -d'@' -f1 | tr -d ' ' || echo "N/A")
  else
    gpu_usage="N/A"
  fi

  # GPU 运行频率
  if [ -f "$GPU_DEVFREQ_PATH/cur_freq" ]; then
    local gpu_freq_hz=$(cat "$GPU_DEVFREQ_PATH/cur_freq" 2>/dev/null)
    gpu_freq_mhz=$([ -n "$gpu_freq_hz" ] && echo $((gpu_freq_hz / 1000000)) || echo "N/A")
  else
    gpu_freq_mhz="N/A"
  fi
}

# 硬件温度获取
get_temp_info() {
  read_temp() {
    local path=$1
    if [ -f "$path" ]; then
      local val=$(cat "$path" 2>/dev/null)
      if [ -n "$val" ] && [ "$val" -gt 0 ]; then
        echo $((val / 1000))
        return
      fi
    fi
    echo "N/A"
  }

  # RK3588 thermal_zone3 对应 CPU A55 小核簇
  temp_cpu_little=$(read_temp "/sys/class/thermal/thermal_zone3/temp")
}

# ==================== 主程序 ====================
init_cpu_stats

while true
do
  calc_per_cpu_usage
  get_npu_per_core_usage
  get_freq_info
  get_mem_info
  get_rga_usage
  get_gpu_info        
  get_temp_info

  clear
  echo "============================================="
  echo "  RK3588 核心实时监控  |  刷新间隔: ${REFRESH_INTERVAL}s"
  echo "  当前时间: $(date '+%Y-%m-%d %H:%M:%S')"
  echo "============================================="
  echo ""
  echo "▶ CPU "
  echo "  核心  | 类型         | 使用率 | 所属簇频率"
  echo "  ------|--------------|--------|-----------"
  for i in 0 1 2 3; do
    printf "  CPU%-2d | %-10s   | %3d%%   | %s MHz\n" \
      "$i" "${core_type[$i]}" "${cpu_usage[$i]}" "$cpu0_freq_mhz"
  done
  echo "  ------|--------------|--------|-----------"
  for i in 4 5; do
    printf "  CPU%-2d | %-10s   | %3d%%   | %s MHz\n" \
      "$i" "${core_type[$i]}" "${cpu_usage[$i]}" "$cpu4_freq_mhz"
  done
  echo "  ------|--------------|--------|-----------"
  for i in 6 7; do
    printf "  CPU%-2d | %-10s | %3d%%   | %s MHz\n" \
      "$i" "${core_type[$i]}" "${cpu_usage[$i]}" "$cpu6_freq_mhz"
  done
  echo ""
  echo "▶ NPU"
  printf "  Core0:  %3s%%    Core1:  %3s%%    Core2:  %3s%%\n" \
    "$npu_core0" "$npu_core1" "$npu_core2"
  printf "  运行频率: %s MHz\n" "$npu_freq_mhz"
  echo ""
  echo "▶ GPU"
  printf "  负载: %3s%%    运行频率: %s MHz\n" "$gpu_usage" "$gpu_freq_mhz"
  echo ""
  echo "▶ RGA"
  printf "  RGA3_0: %3s%%    RGA3_1: %3s%%    RGA2: %3s%%\n" \
    "$rga3_0" "$rga3_1" "$rga2_0"
  echo ""
  echo "▶ 内存 & DDR"
  printf "  内存总容量:  %5d MB\n" "$mem_total_mb"
  printf "  已使用:      %5d MB (%s%%)\n" "$mem_used_mb" "$mem_usage_pct"
  printf "  DDR 频率:    %s MHz\n" "$ddr_freq_mhz"
  echo ""
  echo "▶ 温度"
  printf "  CPU: %3s°C\n" "$temp_cpu_little"

  sleep "$REFRESH_INTERVAL"
done