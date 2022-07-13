from quixstreaming import ParameterData
import pandas as pd


class QuixFunction:

    # Callback triggered for each new parameter data.
    def on_pandas_frame_handler(self, df: pd.DataFrame):
        print(df.to_string())
