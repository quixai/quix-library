from quixstreaming import QuixStreamingClient, StreamReader
from quixstreaming.models.parametersbufferconfiguration import ParametersBufferConfiguration
from quixstreaming.app import App
from quix_function import QuixFunction
import os

# Quix injects credentials automatically to the client. Alternatively, you can always pass an SDK token manually as an argument.
client = QuixStreamingClient()

print("Opening input topic")
input_topic = client.open_input_topic(os.environ["input"])


# read streams
def read_stream(new_stream: StreamReader):
    print("New stream read:" + new_stream.stream_id)

    buffer_options = ParametersBufferConfiguration()
    buffer_options.time_span_in_milliseconds = 100

    buffer = new_stream.parameters.create_buffer(buffer_options)

    # handle the data in a function to simplify the example
    quix_function = QuixFunction()

    buffer.on_read_pandas += quix_function.on_pandas_frame_handler


# Hook up events before initiating read to avoid losing out on any data
input_topic.on_stream_received += read_stream

# Hook up events before initiating read to avoid losing out on any data
input_topic.on_stream_received += read_stream

# Hook up to termination signal (for docker image) and CTRL-C
print("Listening to streams. Press CTRL-C to exit.")

# Handle graceful exit
App.run()

print("Exiting")
