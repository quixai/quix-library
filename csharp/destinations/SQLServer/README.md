# C# Snowflake Sink

The sample contained in this folder gives an example on how to stream data from Quix to Snowflake, it handles both parameter and event data.

## Requirements / Prerequisites
 - A Snowflake account.

## Environment variables

The code sample uses the following environment variables:

- **Broker__TopicName**: Name of the input topic to read from.
- **Snowflake__ConnectionString**: The Snowflake database connection string. 
  - e.g. account=xxx.north-europe.azure;user=xxx;password=xxx;db=xxx

## Known limitations 
- Binary parameters are not supported in this version
- Stream metadata is not persisted in this version

## Docs
Check out the [SDK docs](https://quix.ai/docs/sdk/introduction.html) for detailed usage guidance

## How to run
Create an account on [Quix](https://portal.platform.quix.ai/self-sign-up?xlink=github) to edit or deploy this application without a local environment setup.
