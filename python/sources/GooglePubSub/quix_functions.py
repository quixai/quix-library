from quixstreaming import StreamWriter, EventData
from datetime import datetime
from google.cloud import pubsub_v1


class QuixFunctions:

    is_connected = False

    def __init__(self, stream_writer: StreamWriter):
        self.stream_writer = stream_writer

    def callback(self, message: pubsub_v1.subscriber.message.Message) -> None:
        print("Sending RAW data event to Quix")

        # build the EventData object
        event_data = EventData(event_id="raw_data", time=datetime.utcnow(), value=message.data.decode("UTF-8"))

        # write the message to Quix as an event
        self.stream_writer.events.write(event_data)

        # ack the message to let google know we've handled it
        message.ack()
