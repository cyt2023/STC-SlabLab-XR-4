import geopandas as gpd
import pandas as pd
import numpy as np
import os
from pyproj import Transformer
from tqdm import tqdm
import json

HERE = os.path.dirname(__file__)
ChinaGeoJsonPath = os.path.join(HERE, 'exampleData', 'chinaGeoJson.json')
exampleAQIPath = os.path.join(HERE, 'exampleData', 'data_merged', 'LOC_AQI_0.csv')
# 保存在本地的geoJson数据
chinaGeoData = gpd.read_file(ChinaGeoJsonPath)
chinaGeoData = chinaGeoData.set_crs("EPSG:4326", allow_override=True)

# EPSG转换器
transformer = Transformer.from_crs("EPSG:4326", "EPSG:3857", always_xy=True)

js = chinaGeoData
js_box = js.geometry.total_bounds
js_box[0],js_box[1] = transformer.transform(js_box[0],js_box[1])
js_box[2],js_box[3] = transformer.transform(js_box[2],js_box[3])

width = 175
height = 175
expand_ratio = 2
layer_count = int(os.environ.get('VOLUMESTC_LAYER_COUNT', '8'))
time_width = int(os.environ.get('VOLUMESTC_TIME_WIDTH', '552'))
width = int(os.environ.get('VOLUMESTC_GRID_WIDTH', width))
height = int(os.environ.get('VOLUMESTC_GRID_HEIGHT', height))
expand_ratio = int(os.environ.get('VOLUMESTC_EXPAND_RATIO', expand_ratio))
startTime = 0
endTime = 1
variogram_model = 'linear'

grid_lon = np.linspace(js_box[0], js_box[2], width)
grid_lat = np.linspace(js_box[1], js_box[3], height)

# AQI数据中最大值为 500 ，最小值为 12。
MAX_VAL= 500
MIN_VAL = 12

target_val = pd.read_csv(exampleAQIPath)
location_np = target_val.to_numpy()[:, 0:2]
np_lng, np_lat = transformer.transform(np.array(target_val['lng'].values), np.array(target_val['lat'].values))


prev_z1 = []
MAX_NP_LNG = max(np_lng)
MIN_NP_LNG = min(np_lng)
print(MIN_NP_LNG)

temp_min_val_after_interpolation = 10000
temp_max_val_after_interpolation = -10000

def Kriging(vals):
    from pykrige.ok import OrdinaryKriging    
    OK = OrdinaryKriging(np_lng, np_lat, np.array(vals),  
                                        variogram_model=variogram_model, 
                                        exact_values=False)
    z1, ss1 = OK.execute('grid', grid_lon, grid_lat)
    mean_kriging = np.mean(z1)
    max_kriging = np.max(z1)
    if(max_kriging == mean_kriging):
        z1 = []
        # print('Error! max_kriging == mean_kriging')
    return z1

for slice in range(0, layer_count):
    res = []
    print(f'slice:{slice + 1}/{layer_count}, time width:{time_width}, grid:{width}x{height}, expand:{expand_ratio}')
    startTime = 0 + time_width * slice
    endTime = 0 + time_width * (slice+1)
    for i in tqdm(range(startTime, endTime)):
        targetAQIPath = os.path.join(HERE, 'exampleData', 'data_merged', f'LOC_AQI_{i}.csv')
        vals = pd.read_csv(targetAQIPath)
        temp_res = Kriging(vals['val'].values)
        if(len(temp_res) == 0):
            temp_res = prev_z1
        prev_z1 = temp_res
        # 加速计算并降低准度：等比放大，175*175 扩展为 350*350
        temp_res = np.kron(temp_res, np.ones((expand_ratio, expand_ratio)))
        res.append(temp_res)

    temp_res = np.array(res).flatten()

    ChinaInCompJsonPath = os.path.join(HERE, 'exampleData', 'chinaChange.json')
    china = gpd.read_file(ChinaInCompJsonPath, crs='EPSG:4326')  # 非完整的中国地图，排除南海诸岛等GeoJson中未封闭区域

    china_total = gpd.GeoSeries([china.iloc[:-1, :].unary_union], crs='EPSG:4326')
    china_total_new = china_total.to_crs(epsg=3857)

    grid_lon_for_clip = np.linspace(js_box[0], js_box[2], width * expand_ratio)
    grid_lat_for_clip = np.linspace(js_box[1], js_box[3], height * expand_ratio)

    # 转换成网格
    xgrid, ygrid = np.meshgrid(grid_lon_for_clip, grid_lat_for_clip)

    df_grid = pd.DataFrame(dict(long=xgrid.flatten(), lat=ygrid.flatten()))
    df_grid_geo = gpd.GeoDataFrame(df_grid, geometry=gpd.points_from_xy(df_grid["long"], df_grid["lat"]),
                                crs='EPSG:3857')
    js_kde_clip = gpd.clip(df_grid_geo, china_total_new)

    china = None
    china_total = None
    grid_lon_for_clip = None
    grid_lat_for_clip = None
    xgrid = None
    ygrid = None
    df_grid = None

    js_kde_clip['val'] = False
    df_grid_geo['val'] = True
    df_grid_geo.update(js_kde_clip)

    # 开始裁切
    temp_res[np.tile(df_grid_geo['val'].to_numpy(), (endTime - startTime)).tolist()] = 0.0
    df_grid_geo = None
    js_kde_clip = None

    ##################################################################

    # 我们发现，转换为3D材质后，Unity坐标系设置不同，需要反转Res

    # 将列表分为 timeRange 组
    sub_arrays = np.array_split(temp_res, endTime - startTime)

    # 反转分组后的sub_arrays
    sub_arrays.reverse()

    # 连接反转后的数组
    temp_res = np.concatenate(sub_arrays)

    sub_arrays = None

    ##################################################################
    # 导出

    print('Start Output')

    jsonRes = {
        'xLength': width * expand_ratio,
        'yLength': height * expand_ratio,
        'zLength': endTime - startTime,
        'data': temp_res.tolist()
    }
    temp_res = []

    jsonResStr = json.dumps(jsonRes)
    jsonRes = {}

    if(not os.path.exists(os.path.join(HERE, 'InterpolateResult'))):
        os.makedirs(os.path.join(HERE, 'InterpolateResult'))

    OutputPath = os.path.join(HERE, 'InterpolateResult', f'volume_{variogram_model}_timeWidth_{startTime}_{endTime}_definition_{width}_{height}_expand_ratio_{expand_ratio}_sill_test.json')
    f = open(OutputPath, 'w')
    f.write(jsonResStr)
    jsonResStr = ''
    f.close()

