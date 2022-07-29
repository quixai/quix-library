from quixstreaming import QuixStreamingClient, StreamEndType, StreamReader
from quixstreaming.app import App
from quix_function import QuixFunction
import os
from datetime import datetime

# Quix injects credentials automatically to the client.
# Alternatively, you can always pass an SDK token manually as an argument.
client = QuixStreamingClient()

print("Opening input and output topics")
input_topic = client.open_input_topic(os.environ["input"], "default-consumer-group")
output_topic = client.open_output_topic(os.environ["output"])


# Callback called for each incoming stream
def read_stream(input_stream: StreamReader):

    # Create a new stream to output data
    output_stream = output_topic.create_stream(input_stream.stream_id + "-snowplow-transformed{}"
                                               .format(datetime.now().strftime("%d-%m-%Y %X")))
    output_stream.properties.parents.append(input_stream.stream_id)

    # handle the data in a function to simplify the example
    quix_function = QuixFunction(input_stream, output_stream)

    # React to new data received from input topic.
    input_stream.events.on_read += quix_function.on_event_data_handler

    # When input stream closes, we close output stream as well. 
    def on_stream_close(endType: StreamEndType):
        output_stream.close()
        print("Stream closed:" + output_stream.stream_id)

    input_stream.on_stream_closed += on_stream_close


# Hook up events before initiating read to avoid losing out on any data
input_topic.on_stream_received += read_stream

# Hook up to termination signal (for docker image) and CTRL-C
print("Listening to streams. Press CTRL-C to exit.")

# Handle graceful exit of the model.
App.run()
