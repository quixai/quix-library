from quixstreaming import StreamReader, StreamWriter, EventData
import pandas as pd
from snowplow_analytics_sdk import event_transformer as et
from datetime import datetime


class QuixFunction:
    def __init__(self, input_stream: StreamReader, output_stream: StreamWriter):
        self.input_stream = input_stream
        self.output_stream = output_stream

    # Callback triggered for each new event.
    def on_event_data_handler(self, data: EventData):

        # use the snowplow sdk to transform the incoming data
        tx = et.transform(data.value)

        # convert to data frame
        df = pd.DataFrame.from_dict(tx)

        # add a time stamp to the row
        df["time"] = datetime.now()

        # publish to the stream
        self.output_stream.parameters.buffer.write(df)

        print("Published {}".format(df["event_name"].values[0]))
