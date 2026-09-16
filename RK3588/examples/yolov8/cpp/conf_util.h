/*
================================================================================
【文件总说明】
文件：conf_util.h
功能：极简 key=value 配置文件读取接口（C 实现，C/C++ 通用）
用途：板端所有程序（rknn_yolov8_stream / rknn_udp_uart）统一从 llm.conf 读参数

接口约定：
- conf_get_str(): 取字符串值，成功 0 / 失败 -1（键不存在或文件打不开）
- conf_get_int(): 取整数值，失败返回调用方给的默认值
- 格式: 每行 key=value，# 开头为注释，键值两侧空白自动去除
================================================================================
*/

#ifndef CONF_UTIL_H
#define CONF_UTIL_H

#ifdef __cplusplus
extern "C" {
#endif

/**
 * @brief 从 .conf 文件读取字符串值
 * @param path     配置文件路径
 * @param key      键名（精确匹配）
 * @param out      输出缓冲区
 * @param out_len  缓冲区大小
 * @return 0 成功，-1 未找到
 */
int conf_get_str(const char *path, const char *key, char *out, int out_len);

/**
 * @brief 从 .conf 文件读取整数值
 * @param path     配置文件路径
 * @param key      键名
 * @param defval   未找到时的默认值
 * @return 解析出的整数或 defval
 */
int conf_get_int(const char *path, const char *key, int defval);

#ifdef __cplusplus
}
#endif

#endif // CONF_UTIL_H
