from quixstreaming import QuixStreamingClient
import time
import datetime
import math
import os


# Create a client. Client helps you to create input reader or output writer for specified topic.
client = QuixStreamingClient('{placeholder:token}')
# temporary (needed for dev)
client.api_url = "https://portal-api.dev.quix.ai"

# TODO
output_topic = client.open_output_topic(os.environ["output"])

stream = output_topic.create_stream()
stream.properties.name = "Hello World python stream"
stream.parameters.add_definition("ParameterA").set_range(-1.2, 1.2)
stream.parameters.buffer.time_span_in_milliseconds = 100

print("Sending values for 30 seconds.")

for index in range(0, 3000):
    stream.parameters \
        .buffer \
        .add_timestamp(datetime.datetime.utcnow()) \
        .add_value("ParameterA", math.sin(index / 200.0) + math.sin(index) / 5.0) \
        .write()
    time.sleep(0.01)

print("Closing stream")
stream.close()
