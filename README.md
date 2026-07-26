# UsmDivinerSharp
UsmDiviner 的 C# 重写版本，该程序是一款用于 CRI USM 视频资产的密钥恢复工具。

与原项目相比，本项目专注于密钥恢复，因此移除了流提取部分。

性能也是本项目的重要考量因素。比起原项目，本项目的密钥恢复速度有显著提升，性能提升最高可达数十倍。

C# reimplemtion of UsmDiviner, a key recovery tool for CRI USM video assets.

Compared to the original project, this project focuses on key recovery, thus removing the stream extraction component.

Performance is also a key consideration in this project; the key recovery speed is significantly faster than the original project, with performance improvements of up to tens of times.

## Credits
- The core key recovery algorithm originates from the Python project [Senkin219/UsmDiviner](https://github.com/Senkin219/UsmDiviner), which is licensed under GPLv3.
- Embeds the `TensorPrimitives.IBinaryOperator` and `TensorPrimitives.Xor` portions of the `System.Numerics.Tensors` library, which is licensed under the MIT license.