# -*- coding: utf-8 -*-
import pandas as pd
import numpy as np
from tqdm import *
import os
from pyproj import Transformer
import time
import geopandas as gpd

HERE = os.path.dirname(__file__)
layer_count = int(os.environ.get('VOLUMESTC_LAYER_COUNT', '8'))
time_width = int(os.environ.get('VOLUMESTC_TIME_WIDTH', '552'))
grid_width = int(os.environ.get('VOLUMESTC_GRID_WIDTH', '175'))
grid_height = int(os.environ.get('VOLUMESTC_GRID_HEIGHT', '175'))
expand_ratio = int(os.environ.get('VOLUMESTC_EXPAND_RATIO', '2'))
spatial_window_radius = int(os.environ.get('VOLUMESTC_SPATIAL_RADIUS', '2'))
temporal_window_radius = int(os.environ.get('VOLUMESTC_TEMPORAL_RADIUS', '24'))

for index in range(0, layer_count):
    print(f'index:{index + 1}/{layer_count}, time width:{time_width}, grid:{grid_width}x{grid_height}, expand:{expand_ratio}')
    interpolateFileName = f"volume_linear_timeWidth_{0 + index * time_width}_{0 + (index+1) * time_width}_definition_{grid_width}_{grid_height}_expand_ratio_{expand_ratio}_sill_test"
    # fileName = 'volume_linear_timeWidth_0_512_definition_175_175_expand_ratio_2_sill'
    importDataPath = os.path.join(HERE, 'InterpolateResult', f'{interpolateFileName}.json')

    pd_test_pred = pd.read_json(importDataPath)
    data = pd_test_pred['data']
    xLength = pd_test_pred['xLength'].values[0]
    yLength = pd_test_pred['yLength'].values[0]
    zLength = pd_test_pred['zLength'].values[0]
    if(index != 0):
        prevFileName = f"volume_linear_timeWidth_{0 + (index-1) * time_width}_{0 + (index) * time_width}_definition_{grid_width}_{grid_height}_expand_ratio_{expand_ratio}_sill_test"
        prevDataPath = os.path.join(HERE, 'InterpolateResult',  f'{prevFileName}.json')
        prev_pd_test_pred = pd.read_json(prevDataPath)
    if(index != layer_count - 1):
        nextFileName = f"volume_linear_timeWidth_{0 + (index+1) * time_width}_{0 + (index+2) * time_width}_definition_{grid_width}_{grid_height}_expand_ratio_{expand_ratio}_sill_test"
        nextDataPath = os.path.join(HERE, 'InterpolateResult', f'{nextFileName}.json')
        next_pd_test_pred = pd.read_json(nextDataPath)

    # TODO: 优化
    def smooth3d_mean(zLength,xLength,yLength):
        _data3d = np.array(data).reshape(zLength,xLength,yLength)
        temp_data = np.array(data).reshape(zLength,xLength,yLength)
        _spatial_window_radius = spatial_window_radius
        _temporal_window_radius = temporal_window_radius
        for t in tqdm(range(zLength)):
            start_t = max(0, t - _temporal_window_radius)
            end_t = min(zLength, t + _temporal_window_radius)
            if(index != 0 and end_t != t + _temporal_window_radius):
                prev_data = np.array(prev_pd_test_pred['data']).reshape(zLength,xLength,yLength)
            if(index != 7 and start_t != t - _temporal_window_radius):
                next_data = np.array(next_pd_test_pred['data']).reshape(zLength,xLength,yLength)
            for x in range(xLength):
                start_x = max(0, x - _spatial_window_radius)
                end_x = min(xLength, x + _spatial_window_radius)
                for y in range(yLength):
                    start_y = max(0, y - _spatial_window_radius)
                    end_y = min(yLength, y + _spatial_window_radius)
                    window = temp_data[start_t:end_t, start_x:end_x, start_y:end_y]
                    if(index != layer_count - 1 and start_t != t - _temporal_window_radius):
                        window = np.append(window.flatten(), next_data[zLength - temporal_window_radius + t: zLength, start_x:end_x, start_y:end_y].flatten(), axis=0)
                    if(index != 0 and end_t != t + _temporal_window_radius):
                        window = np.append(window.flatten(), prev_data[0: t + _temporal_window_radius - zLength, start_x:end_x, start_y:end_y].flatten(), axis=0)
                    _data3d[t][x][y] = window.mean()
        return _data3d

    def clipedChinaFrame(data):
        # 裁切中国地图
        temp_res = data

        ChinaInCompJsonPath = os.path.join(HERE, 'exampleData', 'chinaChange.json')
        ChinaGeoJsonPath = os.path.join(HERE, 'exampleData', 'chinaGeoJson.json')
        china = gpd.read_file(ChinaInCompJsonPath, crs='EPSG:4326')  # 非完整的中国地图，排除南海诸岛等GeoJson中未封闭区域
        # 使用本地geoJson数据
        chinaGeoData = gpd.read_file(ChinaGeoJsonPath, crs='EPSG:4326')
        china_total = gpd.GeoSeries([china.iloc[:-1, :].unary_union], crs='EPSG:4326')
        china_total_new = china_total.to_crs(epsg=3857)

        js = chinaGeoData
        transformer = Transformer.from_crs("EPSG:4326", "EPSG:3857", always_xy=True)
        js_box = js.geometry.total_bounds
        js_box[0],js_box[1] = transformer.transform(js_box[0],js_box[1])
        js_box[2],js_box[3] = transformer.transform(js_box[2],js_box[3])
        china = None
        grid_lon_for_clip = np.linspace(js_box[0], js_box[2], xLength)
        grid_lat_for_clip = np.linspace(js_box[1], js_box[3], yLength)
        # 转换成网格
        xgrid, ygrid = np.meshgrid(grid_lon_for_clip, grid_lat_for_clip)

        df_grid = pd.DataFrame(dict(long=xgrid.flatten(), lat=ygrid.flatten()))
        df_grid_geo = gpd.GeoDataFrame(df_grid, geometry=gpd.points_from_xy(df_grid["long"], df_grid["lat"]),
                                    crs='EPSG:3857')
        js_kde_clip = gpd.clip(df_grid_geo, china_total_new)


        js_kde_clip['val'] = False
        df_grid_geo['val'] = True

        # df_grid_geo中为True的部分会被裁切
        # df_grid_geo中为False的部分表示中国地图
        df_grid_geo.update(js_kde_clip)


        # 裁切
        temp_res[np.tile(df_grid_geo['val'].to_numpy(), (zLength)).tolist()] = 0.0

        return temp_res

    # 三维均值滤波
    startTime = time.time()
    smoooth_res = smooth3d_mean(zLength,xLength,yLength).reshape(xLength * yLength * zLength)
    timeCost = time.time() - startTime
    print(f'timeCost per timestamp:{timeCost / zLength}')

    smoooth_res = clipedChinaFrame(smoooth_res)

    smoooth_res = np.array(smoooth_res)

    def map_values_with_condition(input_array):
        min_value = 1
        max_value = 500

        # 归一化到0~255
        mapped_array = np.where(input_array == 0, 1, ((input_array - min_value) / (max_value - min_value)) * 249 + 5)

        # 将数据类型转换为整数
        mapped_array = np.round(mapped_array).astype(int)

        return mapped_array
    smoooth_res = map_values_with_condition(smoooth_res)
    smoooth_res = smoooth_res.astype(np.uint8)

    ##################################################################
    # 导出

    if(not os.path.exists(os.path.join(HERE, 'UnityRawData'))):
        os.makedirs(os.path.join(HERE, 'UnityRawData'))

    fileName = f'{interpolateFileName}_smooth_s_{spatial_window_radius}_t_{temporal_window_radius}_smooth_correct.raw'
    outputRawPath = os.path.join(HERE, 'UnityRawData', fileName)
    smoooth_res.tofile(outputRawPath)
    # xLength = json_res_pd['xLength'].values[0]
    # yLength = json_res_pd['yLength'].values[0]
    # zLength = json_res_pd['zLength'].values[0]
    json_res_pd = None
    print(f'x:{xLength}')
    print(f'y:{yLength}')
    print(f'z:{zLength}')

    # 编写并导出配置文件ini
    outputIniPath = os.path.join(HERE, 'UnityRawData', f'{fileName}.ini')
    with open(outputIniPath, 'w') as f:
        ini = f'dimx:{xLength} \n' + f'dimy:{yLength} \n' + f'dimz:{zLength} \n' + 'skip:0 \nformat:uint8'
        f.write(ini)
