from quixstreaming import QuixStreamingClient
from quixstreaming.app import App
from datetime import datetime, timezone
import requests
import traceback
import pandas as pd
from threading import Thread
import os

# Quix injects credentials automatically to the client. Alternatively, you can always pass an SDK token manually as an argument.
client = QuixStreamingClient()

# should the main loop run?
run = True

# Open the output topic and create the stream
print("Opening output topic")
output_topic = client.open_output_topic(os.environ["output"])


def get_data():
    while run:

        try:
            # Current timestamp
            current_time_i = datetime.now(timezone.utc)

            # Perform API request for NY bikes data
            response = requests.get('https://gbfs.citibikenyc.com/gbfs/en/station_status.json')
            json_response = response.json()

            # Create total dataframe
            df = pd.DataFrame()

            # Iterate over stations
            for station in json_response['data']['stations']:
                # Create station_i dataframe
                df_i = pd.DataFrame({
                    'timestamp': [current_time_i],
                    'id': [int(station['station_id'])],
                    'num_bikes_available': [int(station['num_bikes_available'])],
                    'num_docks_available': [int(station['num_docks_available'])],
                    'num_ebikes_available': [int(station['num_ebikes_available'])],
                    'total_num_bikes_available': [
                        int(station['num_bikes_available']) + int(station['num_ebikes_available'])]})

                # Access station_i topic and write df_i
                output_stream_i = output_topic.get_or_create_stream(
                    "NYBikes-StationID-{}".format(station['station_id']))
                output_stream_i.parameters.write(df_i)

                # Add data from station i to total df
                df = df.append(df_i)

            # Aggregated for all stations
            cols_list = ['num_bikes_available', 'num_docks_available', 'num_ebikes_available',
                         'total_num_bikes_available']
            df = df[cols_list].sum().reset_index().set_index('index').T
            df["Timestamp"] = current_time_i

            # Access station_i topic and write df_i
            output_stream = output_topic.get_or_create_stream("All-Stations-NY-Bikes")
            output_stream.parameters.write(df)

            # How long did the Request and transformation take
            print(current_time_i, 'NY bikes: ', df['total_num_bikes_available'].iloc[0])

        except Exception:
            print(traceback.format_exc())


def before_shutdown():
    global run
    # Stop the main loop
    run = False


def main():
    thread = Thread(target=get_data)
    thread.start()

    # If it shutsdown for whatever reason, update run to False
    App.run(before_shutdown=before_shutdown)

    # wait for worker thread to end
    thread.join()
    print("Exiting")


if __name__ == "__main__":
    main()
