from quixstreaming import StreamReader, StreamWriter
import quixstreaming.state as st
import datetime
import pandas as pd

pd.set_option('display.max_rows', None)
pd.set_option('display.max_columns', None)
pd.set_option('display.width', 1000)
pd.set_option('display.colheader_justify', 'center')
pd.set_option('display.precision', 3)

class QuixFunction:
    def __init__(self, input_stream: StreamReader, output_stream: StreamWriter, title_contains_word: str):
        self.input_stream = input_stream
        self.output_stream = output_stream
        self.title_contains_word = title_contains_word
        self.storage = st.LocalFileStorage()
        self.reported = []
        if "reddit" in self.storage.getAllKeys():
            self.reported = self.storage.get("reddit")

    def on_parameter_dataframe_handler(self, data: pd.DataFrame):
        df = data.filter(items=['time', 'title', 'shortlink', 'TAG__id'])
        df = df[df['title'].str.contains(self.title_contains_word)]
        df = df[~df['TAG__id'].isin(self.reported)]
        if len(df) == 0:
            print("No matching submission found for " + str(self.title_contains_word));
            return
        else:
            print(str(len(df)) + " matching submission found")
        
        for index, row in df.iterrows():
            submission_id = row["TAG__id"]
            print("  Reporting " + submission_id)
            self.reported.append(submission_id)            
            self.output_stream.parameters.buffer.add_timestamp(datetime.datetime.utcnow()).add_value("text", row["shortlink"]).write()
        self.storage.set("reddit", self.reported)

