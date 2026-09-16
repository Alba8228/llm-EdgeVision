/*
 * 文件：udp_uart1.c
 * 功能：香橙派5max开发板 UDP <--> UART 双向转发程序
 *       1) 接收远程设备通过UDP发送的16字节控制协议包，转发给MCU（UART1）
 *       2) 读取MCU通过UART1回显的数据，打印到终端并回传给UDP客户端
 *
 * 整体工作流程：
 * 1. main()：从 ./llm.conf 读 uart_dev / uart_baud / udp_port（参数收敛，命令行无参数）
 * 2. uart_open()：打开串口设备并配置（波特率白名单映射，8N1）
 * 3. udp_server_init()：创建UDP socket并绑定端口
 * 4. main循环（select多路复用，同时监听UDP和UART）：
 *    4.1 UDP可读：recvfrom()接收16字节包 -> write()转发给MCU，记录客户端地址
 *    4.2 UART可读：read()读取MCU回显 -> 打印到终端 + sendto()回传给UDP客户端
 *
 * 协议格式（16字节）：
 *   [0-3]   包头：0xAA 0xBB 0xAA 0xBB
 *   [4-5]   steer 转向 (int16 LE)
 *   [6]     throttle 油门 (uint8)
 *   [7]     brake 刹车 (uint8)
 *   [8]     gear 档位 (uint8)
 *   [9]     mode 模式 (uint8)
 *   [10-11] tilt 俯仰 (int16 LE)
 *   [12-13] pan 朝向 (int16 LE)
 *   [14-15] checksum 校验和 (uint16 LE) = bytes[0..13] 累加
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <fcntl.h>
#include <termios.h>
#include <errno.h>
#include <stdint.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <sys/select.h>

#include "conf_util.h"

#define PACK_LEN    16
#define UDP_BUF_LEN 256   /* UDP接收缓冲区，大于PACK_LEN以检测超长包 */

/**
 * @brief 波特率数值 → termios speed_t 常量（白名单映射，非法值返回0）
 * @param baud 波特率数值，如 115200
 * @return 对应 speed_t 常量；不支持的波特率返回 0
 */
static speed_t baud_to_speed(int baud)
{
    switch (baud)
    {
        case 9600:    return B9600;
        case 19200:   return B19200;
        case 38400:   return B38400;
        case 57600:   return B57600;
        case 115200:  return B115200;
        case 230400:  return B230400;
        case 460800:  return B460800;
        case 921600:  return B921600;
        default:      return 0;
    }
}

/**
 * @brief 打开并配置串口（8N1 原始模式）
 * @param dev  串口设备路径，如 /dev/ttyS1
 * @param baud 波特率 speed_t 常量
 * @return 成功返回 fd，失败返回 -1
 */
int uart_open(const char *dev, speed_t baud)
{
    int fd = open(dev, O_RDWR | O_NOCTTY | O_NDELAY);
    if (fd < 0)
    {
        perror("open uart failed");
        return -1;
    }
    fcntl(fd, F_SETFL, 0);

    struct termios opt;
    tcgetattr(fd, &opt);

    cfsetispeed(&opt, baud);
    cfsetospeed(&opt, baud);

    opt.c_cflag &= ~PARENB;    // 无校验
    opt.c_cflag &= ~CSTOPB;    // 1停止位
    opt.c_cflag &= ~CSIZE;
    opt.c_cflag |= CS8;        // 8数据位
    opt.c_cflag |= CREAD | CLOCAL;

    opt.c_lflag &= ~(ICANON | ECHO | ECHOE | ISIG);
    opt.c_iflag &= ~(IXON | IXOFF | IXANY);
    opt.c_oflag &= ~OPOST;

    opt.c_cc[VTIME] = 0;
    opt.c_cc[VMIN] = 1;

    tcsetattr(fd, TCSANOW, &opt);
    tcflush(fd, TCIOFLUSH);
    return fd;
}

/**
 * @brief 向串口可靠写入指定长度数据
 *        write()对慢速设备(如串口)可能只写入部分字节，需循环写直到全部完成
 * @param fd  串口文件描述符
 * @param buf 待发送数据
 * @param len 待发送长度
 * @return 成功返回已写入字节数(等于len)，失败返回-1
 */
ssize_t uart_write_n(int fd, const uint8_t *buf, size_t len)
{
    size_t total = 0;
    while (total < len)
    {
        ssize_t n = write(fd, buf + total, len - total);
        if (n < 0)
        {
            if (errno == EINTR) continue;   /* 被信号中断，重试 */
            return -1;
        }
        if (n == 0) break;                 /* 不应发生，防御性处理 */
        total += n;
    }
    return (ssize_t)total;
}

// 计算校验和：前14字节累加
uint16_t calc_checksum(uint8_t *buf)
{
    uint16_t sum = 0;
    for(int i = 0; i < 14; i++)
    {
        sum += buf[i];
    }
    return sum;
}

// 填充协议数据包
void fill_packet(uint8_t *packet,
                  int16_t steer,    // 转向
                  uint8_t throttle, // 油门
                  uint8_t brake,    // 刹车
                  uint8_t gear,     // 换挡
                  uint8_t mode,     // 模式
                  int16_t tilt,     // 俯仰
                  int16_t pan)      // 朝向
{
    // 包头
    packet[0] = 0xAA;
    packet[1] = 0xBB;
    packet[2] = 0xAA;
    packet[3] = 0xBB;

    // 转向 int16 小端
    packet[4] = steer & 0xFF;
    packet[5] = (steer >> 8) & 0xFF;

    packet[6] = throttle;
    packet[7] = brake;
    packet[8] = gear;
    packet[9] = mode;

    // 俯仰 int16 小端
    packet[10] = tilt & 0xFF;
    packet[11] = (tilt >> 8) & 0xFF;
    // 朝向 int16 小端
    packet[12] = pan & 0xFF;
    packet[13] = (pan >> 8) & 0xFF;

    // 校验和
    uint16_t crc = calc_checksum(packet);
    packet[14] = crc & 0xFF;
    packet[15] = (crc >> 8) & 0xFF;
}

/**
 * @brief 创建并绑定UDP服务器socket
 * @param port 监听端口号
 * @return 成功返回socket fd，失败返回-1
 */
int udp_server_init(int port)
{
    int sock = socket(AF_INET, SOCK_DGRAM, 0);
    if (sock < 0)
    {
        perror("socket");
        return -1;
    }

    struct sockaddr_in addr;
    memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    addr.sin_port = htons(port);

    if (bind(sock, (struct sockaddr *)&addr, sizeof(addr)) < 0)
    {
        perror("bind");
        close(sock);
        return -1;
    }

    return sock;
}

/**
 * @brief 程序入口：读 llm.conf 的 uart 段参数，打开串口+监听UDP，select 转发循环
 * @return 0 正常（实际不会退出），-1 初始化失败
 */
int main()
{
    // 参数收敛到 ./llm.conf（与 rknn_yolov8_stream 同一份文件），默认值保持原硬编码行为
    static const char *CONF = "llm.conf";
    char uart_dev[128] = {0};
    if (conf_get_str(CONF, "uart_dev", uart_dev, sizeof(uart_dev)) != 0 || uart_dev[0] == '\0')
        snprintf(uart_dev, sizeof(uart_dev), "/dev/ttyS1");
    int baud_val  = conf_get_int(CONF, "uart_baud", 115200);
    int udp_port  = conf_get_int(CONF, "udp_port", 12345);

    speed_t baud = baud_to_speed(baud_val);
    if (baud == 0)
    {
        printf("[uart] invalid uart_baud=%d in %s (support: 9600/19200/38400/57600/115200/230400/460800/921600)\n",
               baud_val, CONF);
        return -1;
    }

    int uart_fd = uart_open(uart_dev, baud);
    if (uart_fd < 0) return -1;
    printf("Open %s @%d success!\n", uart_dev, baud_val);

    int udp_sock = udp_server_init(udp_port);
    if (udp_sock < 0)
    {
        close(uart_fd);
        return -1;
    }
    printf("UDP server listening on port %d...\n", udp_port);
    printf("Bidirectional: UDP <-> UART (select)\n\n");

    uint8_t udp_buf[UDP_BUF_LEN];
    uint8_t uart_buf[256];
    char ip_str[INET_ADDRSTRLEN];           /* inet_ntop输出缓冲区 */
    struct sockaddr_in remote_addr;
    socklen_t addr_len = sizeof(remote_addr);
    int has_client = 0;                     /* 是否已收到过UDP包，即remote_addr是否有效 */

    /* select所需：取两个fd中的最大值 */
    int maxfd = (udp_sock > uart_fd) ? udp_sock : uart_fd;

    while (1)
    {
        fd_set readfds;
        FD_ZERO(&readfds);
        FD_SET(udp_sock, &readfds);
        FD_SET(uart_fd, &readfds);

        int ret = select(maxfd + 1, &readfds, NULL, NULL, NULL);
        if (ret < 0)
        {
            if (errno == EINTR) continue;   /* 被信号中断，重试 */
            perror("select");
            continue;
        }

        /* ===== 1. UDP可读：接收远程数据包并转发给MCU ===== */
        if (FD_ISSET(udp_sock, &readfds))
        {
            /* 用大缓冲区接收，避免超长包被截断后误判为有效包 */
            ssize_t n = recvfrom(udp_sock, udp_buf, UDP_BUF_LEN, 0,
                                 (struct sockaddr *)&remote_addr, &addr_len);
            if (n < 0)
            {
                perror("recvfrom");
            }
            else
            {
                /* inet_ntop可重入，替代废弃的inet_ntoa */
                inet_ntop(AF_INET, &remote_addr.sin_addr, ip_str, sizeof(ip_str));
                uint16_t rport = ntohs(remote_addr.sin_port);

                if (n != PACK_LEN)
                {
                    printf("[UDP] ignore packet from %s:%d (invalid length %zd)\n",
                           ip_str, rport, n);
                }
                else
                {
                    /* 记录客户端地址，后续UART回显时sendto到此地址 */
                    has_client = 1;
                    /* 可靠写入：循环直到16字节全部发完 */
                    ssize_t w = uart_write_n(uart_fd, udp_buf, PACK_LEN);
                    printf("[UDP->UART] from %s:%d, %zd bytes -> uart write %zd\n",
                           ip_str, rport, n, w);
                }
            }
        }

        /* ===== 2. UART可读：读取MCU回显，打印终端 + 回传UDP客户端 ===== */
        if (FD_ISSET(uart_fd, &readfds))
        {
            ssize_t n = read(uart_fd, uart_buf, sizeof(uart_buf) - 1);
            if (n > 0)
            {
                uart_buf[n] = '\0';
                /* 打印到终端 */
                printf("[UART->MCU echo] %s", uart_buf);
                /* 回传给UDP客户端（需已有客户端地址） */
                if (has_client)
                {
                    ssize_t s = sendto(udp_sock, uart_buf, n, 0,
                                       (struct sockaddr *)&remote_addr, addr_len);
                    printf("[UART->UDP] echo %zd bytes back to %s:%d\n",
                           s, ip_str, ntohs(remote_addr.sin_port));
                }
            }
            else if (n == 0)
            {
                /* 串口不应返回EOF，出现则说明驱动异常，打印警告 */
                printf("[UART] read returned 0 (EOF), device may be disconnected\n");
            }
            else if (errno != EAGAIN)
            {
                perror("read uart");
            }
        }
    }

    close(uart_fd);
    close(udp_sock);
    return 0;
}