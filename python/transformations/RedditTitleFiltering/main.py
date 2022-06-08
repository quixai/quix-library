from quixstreaming import QuixStreamingClient, StreamEndType, StreamReader
from quixstreaming.app import App
from quix_function import QuixFunction
import os

subreddit = os.environ["subreddit"].lower()
title_contains_word = os.environ["titlecontainsword"]
version = os.environ["Quix__Project__Git__CommitRef"]
print("Version: " + version)


# Quix injects credentials automatically to the client. Alternatively, you can always pass an SDK token manually as an argument.
client = QuixStreamingClient()

print("Opening input and output topics")
input_topic = client.open_input_topic(os.environ["input"], "reddit-" + subreddit+"-"+title_contains_word+"-" + version)
output_topic = client.open_output_topic(os.environ["output"])


# Callback called for each incoming stream
def read_stream(input_stream: StreamReader):
    if input_stream.stream_id.lower() != subreddit:
        print("Stream with id " + input_stream.stream_id + " opened, but is not for subreddit " + subreddit)
        return

    # Create a new stream to output data
    output_stream = output_topic.create_stream(input_stream.stream_id + "-" + title_contains_word)
    output_stream.properties.parents.append(input_stream.stream_id)

    # handle the data in a function to simplify the example
    quix_function = QuixFunction(input_stream, output_stream, title_contains_word)
        
    # React to new data received from input topic.
    input_stream.parameters.on_read_pandas += quix_function.on_parameter_dataframe_handler

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