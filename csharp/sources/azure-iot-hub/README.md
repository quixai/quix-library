# Azure IoT hub sample
The sample contained in this folder gives an example on how to connect to Azure IoT Hub and bridge data into Kafka using the Quix SDK.

![graph](iot-bridge.png?raw=true)

## Environment variables

The code sample uses the following environment variables:

- **output**: This is the ouput topic where data will be written to.
- **connectionString**: This is the Event Hub connection string.
- **eventHubName**: This is the Event Hub name.

### Connection to Quix
In this code snippet, the service connects to the Kafka topic in Quix. Use the Library page in the Quix portal to generate this code for a particular topic in your workspace.
```csharp
 // Create a client factory. The Factory helps you create a QuixStreamingClient (see below) more easily
var client = new Quix.Sdk.Streaming.QuixStreamingClient();

// Create a QuixStreamingClient (using the factory) in order to create new streams for the above configured topic
using var outputTopic = client.OpenOutputTopic("{placeholder:outputTopic}");
```


### Transformation to Quix SDK format
In code example we simply get the whole JSON message and send it as an event with the Quix SDK.

```csharp
var data = Encoding.UTF8.GetString(partitionEvent.Data.Body.ToArray());

stream.Events
	.AddTimestamp(partitionEvent.Data.EnqueuedTime.ToUniversalTime().DateTime)
	.AddValue("message", data)
	.Write();
```
but in a real application, we suggest developing a model to parse this data and send it using parameters to the Quix SDK. 

## Docs

Check out the [SDK docs](https://quix.ai/docs/sdk/introduction.html) for detailed usage guidance

## How to run
This bridge can run locally or in our serverless environment. To learn how to deploy services in Quix, please create an account at [Quix](https://portal.platform.quix.ai/self-sign-up?xlink=github) and see our [docs](https://quix.ai/docs/guides/index.html).

