/*
================================================================================
【文件总说明】
文件：conf_util.c
功能：极简 key=value 配置文件读取实现（C 语言，C/C++ 程序均可链接）

【整体工作流程】
1. conf_get_str(): fopen 逐行 fgets → 跳过空行/#注释 → 找 '=' →
   比较键名（trim 后精确匹配）→ 值 trim 后 strncpy 输出
2. conf_get_int(): 复用 conf_get_str → atoi

关键技术点：
- 纯 stdio 实现，无第三方依赖；每次调用重新打开文件（配置读取均为启动时一次性操作，
  不做缓存，避免多进程共享时的失效问题）
- 同一键出现多次时取最后一次（便于 conf 末尾追加覆盖）
================================================================================
*/

#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <ctype.h>

#include "conf_util.h"

/* 去除字符串首尾空白（原地修改，返回首指针） */
static char *str_trim(char *s)
{
    while (*s && isspace((unsigned char)*s))
        s++;
    char *e = s + strlen(s);
    while (e > s && isspace((unsigned char)e[-1]))
        e--;
    *e = '\0';
    return s;
}

/**
 * @brief 从 .conf 文件读取字符串值
 * @param path     配置文件路径
 * @param key      键名（精确匹配，不含空白）
 * @param out      输出缓冲区
 * @param out_len  缓冲区大小
 * @return 0 成功，-1 未找到/打不开
 */
int conf_get_str(const char *path, const char *key, char *out, int out_len)
{
    FILE *fp = fopen(path, "r");
    if (!fp)
        return -1;

    char line[1024];
    int found = -1;
    size_t klen = strlen(key);

    while (fgets(line, sizeof(line), fp))
    {
        char *s = str_trim(line);
        if (s[0] == '\0' || s[0] == '#')
            continue;
        char *eq = strchr(s, '=');
        if (!eq)
            continue;
        *eq = '\0';
        char *k = str_trim(s);
        char *v = str_trim(eq + 1);
        // 剥离行内注释: 值中 " #" 之后的内容视为注释 (键值本身不含空格+井号)
        char *hash = strstr(v, " #");
        if (hash)
        {
            *hash = '\0';
            v = str_trim(v);
        }
        if (strcmp(k, key) == 0 && strlen(k) == klen)
        {
            // 命中：记录并继续扫描，同名后面的行覆盖前面的
            snprintf(out, out_len, "%s", v);
            found = 0;
        }
    }
    fclose(fp);
    return found;
}

/**
 * @brief 从 .conf 文件读取整数值
 * @param path     配置文件路径
 * @param key      键名
 * @param defval   未找到时的默认值
 * @return 解析出的整数或 defval
 */
int conf_get_int(const char *path, const char *key, int defval)
{
    char buf[64];
    if (conf_get_str(path, key, buf, sizeof(buf)) != 0)
        return defval;
    return atoi(buf);
}
