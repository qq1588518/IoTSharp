## 地理空间基准通稿：轨迹、围栏与空间过滤对比方案

地理空间数据是工业 IoT、车联网、物流冷链和户外设备监控中的高频需求。SonnetDB 已内置 `GEOPOINT`、空间谓词、轨迹聚合和段内 geohash 剪枝，因此地理空间基准单独成篇。

### 通稿

本轮地理空间基准以车辆轨迹为数据模型，在上海附近生成 16 个设备的连续移动点，测试圆形围栏、矩形边界框、轨迹长度和底层 GEOPOINT 范围扫描。该基准验证的不只是数学函数开销，还包括落盘 Segment 上的 geohash block 剪枝是否参与查询。

当前可执行基准聚焦 SonnetDB Core。PostGIS、TimescaleDB + PostGIS、TDengine 地理函数等横向对照需要额外服务编排，已列入后续补充项。

### 对比方案

| 对照对象 | 当前状态 | 方案 |
| --- | --- | --- |
| SonnetDB Core | 已实现，`GeoQueryBenchmark` | `GEOPOINT` 字段 + `geo_within` / `geo_bbox` / `trajectory_length` |
| SonnetDB Server | 待补 | HTTP SQL 查询 + GeoJSON/NDJSON 消费 |
| PostgreSQL/PostGIS | 待补 | `geometry(Point, 4326)` + GiST/SP-GiST 索引 |
| TimescaleDB + PostGIS | 待补 | hypertable + PostGIS 空间索引 |
| TDengine | 待补 | 若使用原生地理函数，需映射等价语义 |
| InfluxDB | 待补 | 通常需 Flux 数学函数或客户端过滤，不作为原生空间库 |

标准查询：

```sql
SELECT time, position
FROM vehicle
WHERE geo_within(position, 31.2304, 121.4737, 1500);

SELECT count(position)
FROM vehicle
WHERE geo_bbox(position, 31.21, 121.45, 31.25, 121.50);

SELECT trajectory_length(position)
FROM vehicle
WHERE device = 'car_00';
```

运行命令：

```powershell
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Geo*
```

### 对比结果

| 方法 | 数据规模 | 平均耗时 | 分配 | 备注 |
| --- | ---: | ---: | ---: | --- |
| SonnetDB `geo_within` | 100k | 29.49 ms | 24.05 MB | 圆形围栏 |
| SonnetDB `geo_bbox + count` | 100k | 24.59 ms | 26.96 MB | 矩形范围 + 聚合 |
| SonnetDB `trajectory_length` | 100k | 1.12 ms | 1.39 MB | 单设备轨迹聚合 |
| SonnetDB GEOPOINT range scan | 100k | 0.465 ms | 0.38 MB | 底层扫描参考 |

### 结论口径

地理空间报告要把“当前 SonnetDB 自身基准”和“未来 PostGIS 横向对照”分开。没有 PostGIS 实测前，不宣称超越 PostGIS；只说明 SonnetDB 已在时序引擎内部完成空间类型、空间函数与轨迹聚合闭环。
