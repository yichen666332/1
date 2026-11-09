# 流程图源文件

毕业设计原始 draw.io 流程图，保留可编辑的 `.drawio` 源文件（XML 格式）。

| 文件 | 内容 | 对应论文图 |
| --- | --- | --- |
| `data-transmission-flow.drawio` | 数据传输流程：下位机采集 → 串口封装 → 上位机解析 → 界面显示 | 第 3 章 数据传输流程 |
| `filtering-algorithm-flow.drawio` | 滤波算法流程：三路并行滤波与组合策略的判定逻辑 | 图 3-3 滤波算法模块流程图 |
| `smart-tuning-module.drawio` | 智能调参闭环：评分 → 特征提取 → LLM 推荐 → 参数回写 | 图 3-5 智能调参模块流程图 |
| `ui-design-module.drawio` | 界面模块划分：通讯连接 / 滤波数据 / 曲线分析 / 智能调参 | 图 3-4 界面设计模块流程图 |

## 打开方式

- 在线：[app.diagrams.net](https://app.diagrams.net/) → File → Open From → Device，选择 `.drawio` 文件；
- VS Code：安装 *Draw.io Integration* 插件后直接双击打开；
- 桌面端：draw.io Desktop。

## 已导出的 PNG

论文中导出的位图版本放在仓库根目录 [`assets/`](../../assets/) 下，命名前缀：

- `architecture-*.png` — 架构与模块流程图
- `chart-*.png` — 波形对比、评分图、仿真图
- `ui-*.png` — 界面截图

完整的架构说明见 [ARCHITECTURE.md](../ARCHITECTURE.md)。
