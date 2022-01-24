from quixstreaming import QuixStreamingClient, StreamEndType, StreamReader
from quixstreaming.app import App
from threshold_function import ThresholdAlert
import os
import pandas as pd

# Create a client. The client helps you to create input reader or output writer for specified topic.
client = QuixStreamingClient('{placeholder:token}')

# Change consumer group to a different constant if you want to run model locally.
print("Opening input and output topics")

# Environment variables
output_topic = client.open_output_topic(os.environ["Quix__Workspace__Id"]+"-"+os.environ["output"])

# Load csv
df =

# Callback called for each incoming stream
def read_stream(input_stream: StreamReader):
    # Create a new stream to output data
    output_stream = output_topic.create_stream(input_stream.stream_id + '-' + os.environ["Quix__Deployment__Name"])
    output_stream.properties.parents.append(input_stream.stream_id)

    print(type(output_stream))

    # handle the data in a function to simplify the example
    quix_function = ThresholdAlert(input_stream, output_stream)

    # React to new data received from input topic.
    input_stream.events.on_read += quix_function.on_event_data_handler
    input_stream.parameters.on_read += quix_function.on_parameter_data_handler

    # When input stream closes, we close output stream as well.
    def on_stream_close(end_type: StreamEndType):
        output_stream.close()
        print("Stream closed:" + output_stream.stream_id)

    input_stream.on_stream_closed += on_stream_close


# Hook up events before initiating read to avoid losing out on any data
input_topic.on_stream_received += read_stream

# Hook up to termination signal (for docker image) and CTRL-C
print("Listening to streams. Press CTRL-C to exit.")

# Handle graceful exit of the model.
App.run()
