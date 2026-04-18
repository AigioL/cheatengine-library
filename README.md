cheatengine-library
===================
@contact : [p-yohann][@][hotmail.fr]

Cheat Engine Library 是首个基于 Cheat Engine 的开源库。Cheat Engine 是一款功能强大的内存编辑软件，原始软件可在 http://cheatengine.org/ 获取。

这个项目的首要目标，是让你能够构建自己的软件，并使用 Auto Assemble、DLL 注入、内存扫描等高级功能。正如你所知，Cheat Engine 本身并没有提供现成的库，这对开发者来说会很不方便。这个库同时支持 x86 和 x64 平台，并且可以配合常见编程语言使用，例如 c#、c++ 和 delphi。

![scanner_c](https://cloud.githubusercontent.com/assets/5822286/3718740/268557f8-163a-11e4-8585-ad3105b28859.png)

## 功能特性
* **管理虚拟 Cheat Engine 表**
 * 手动添加地址
 * 添加 Auto Assemble 脚本
 * 激活、停用、冻结和取消冻结任意地址或脚本
* **注入 Auto Assemble 脚本**
 * **支持的符号：**
    * ALLOC
    * DEALLOC
    * LABEL
    * DEFINE
    * REGISTERSYMBOL
    * UNREGISTERSYMBOL
    * INCLUDE
    * READMEM
    * LOADLIBRARY
    * CREATETHREAD
* **扫描内存以查找特定地址**
 * **扫描类型：**
    * 精确值
    * 小于
    * 大于
    * 介于两值之间
    * 未知初始值
    * 数值增加
    * 数值增加指定值
    * 数值减少
    * 数值减少指定值
    * 已变化
    * 未变化
 * **值类型：**
    * 二进制
    * 字节
    * 2 字节
    * 4 字节
    * 8 字节
    * 单精度浮点
    * 双精度浮点
    * 字符串
  * **内存扫描选项：**
    * 起始地址、结束地址
    * 可写、可执行、写时复制
    * 快速扫描、按对齐与非对齐
    * Unicode、区分大小写

## 最近更新
**最新版本信息请参阅 CHANGELOG 文件**

## 我该从哪里开始？

1. 如果你需要一个开箱即用的方案，请下载：
 * 一个示例项目（delphi、c# 或 c++）
 * 一个用于与该库通信的包装层
 * 发布页提供的库文件：https://github.com/AigioL/cheatengine-library/releases

2. 如果你需要自行构建方案：
 * 下载 Lazarus 64 位或 Lazarus 32 位
 * 复制 library 和 dll 目录

## 是否提供文档？

有，我已经编写了 cheat engine library API 的说明文档。请访问这个链接：https://github.com/fenix01/cheatengine-library/wiki/Guideline