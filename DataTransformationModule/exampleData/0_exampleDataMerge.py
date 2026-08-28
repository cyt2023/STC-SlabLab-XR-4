import pandas as pd
import os
from tqdm import tqdm

# Run only once to merge the example data

HERE = os.path.dirname(__file__)
locationDataPath = os.path.join(HERE, 'locations.json')
valueDataPath = os.path.join(HERE, 'timeseriesdata.json')

locationData = pd.read_json(locationDataPath)
valueData = pd.read_json(valueDataPath)

dataMergedFilePath = os.path.join(HERE, 'data_merged')
if not os.path.exists(dataMergedFilePath):
    os.makedirs(dataMergedFilePath)

data = pd.DataFrame(locationData).drop(['x','y','order'],axis=1)
rid_columns = [int(rid) for rid in data['rid'].values]
value_columns = [str(rid) if str(rid) in valueData.columns else rid for rid in rid_columns]

for index in tqdm(range(8472)):
    output_path = os.path.join(dataMergedFilePath, f'LOC_AQI_{index}.csv')
    if os.path.exists(output_path):
        continue
    data['val'] = valueData.loc[index, value_columns].to_numpy()
    data.to_csv(output_path,index=None)
