from quixstreaming import ParameterData, InputTopic
import pandas as pd
from helpers import CrossStreamStatefullProcessing
import os

class InMemoryView(CrossStreamStatefullProcessing):

    def __init__(self, input_topic: InputTopic):
        super().__init__(input_topic)

    def init_state(self):
        self.set_state(pd.DataFrame())


    # Callback triggered for each new parameter data.
    def on_pandas_frame_handler(self, data_df: pd.DataFrame):

        print(data_df)
        
        self.set_state(data_df)
        
