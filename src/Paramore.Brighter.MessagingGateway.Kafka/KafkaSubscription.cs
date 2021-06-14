﻿#region Licence

/* The MIT License (MIT)
Copyright © 2020 Ian Cooper <ian_hammond_cooper@yahoo.co.uk>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE. */

#endregion

using System;
using Confluent.Kafka;

namespace Paramore.Brighter.MessagingGateway.Kafka
{
    public class KafkaSubscription : Subscription
    {
        /// <summary>
        /// We commit processed work (marked as acked or rejected) when a batch size worth of work has been completed
        /// If the batch size is 1, then there is a low risk of offsets not being committed and therefore duplicates appearing
        /// in the stream, but the latency per request and load on the broker increases. As the batch size rises the risk of
        /// a crashing worker process failing to commit a batch that is then represented rises.
        /// </summary>
        public long CommitBatchSize { get; set; } = 10;

        /// <summary>
        /// If we throw DeferMessageAction beyond the number of times listed in the requecount, move the message into the queue identified by this routing
        /// key and ack to commit the offset for this consumer on the topic, so it is not re-attempted 
        /// </summary>
        public RoutingKey DeadLetterQueue { get; set; }

        /// <summary>
        /// Throwing a DeferMessageAction will ack the message and publish it again, to the delay queue
        /// Setting this property creates the topic defined by DeferQueueTopic, if MakeChannel is set to OnMissingChannel.Create.
        /// Otherwise, we assume that you create it when creating other infrastructure
        /// Usage:=
        /// By throwing a DeferMessageAction from a handler, expected Brighter behaviour is to ack the topic, and republish with a delay
        /// Brighter republishes to the same queue again, after the delay period. Note that as this is a republish, consumers that have already seen the
        /// message will receive a duplicate. In addition, if a queue was first in, first out that ordering will now be broken by the republish and consumers
        /// cannot rely on messages being in order.
        /// Kafka has no delay queue mechanism. However, we can emulate this by publishing to a defer queue, reading from the defer queue at a polling
        /// interval equivalent to the delay, and then republishing to the topic from the defer queue worker.
        /// We create the topic according to the MakeChannel property, so if the producer/consumer do not auto-declare topics, you will need to create the
        /// required defer queue
        /// The name of the defer queue. If not set will default to:
        /// {RoutingKey}_Defer_{RequeueDelayInMs}
        /// </summary>
        public RoutingKey DeferQueueTopic { get; set; }

        /// <summary>
        /// Only one consumer in a group can read from a partition at any one time; this preserves ordering
        /// We do not default this value, and expect you to set it
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// Default to read only committed messages, change if you want to read uncommited messages. May cause duplicates.
        /// </summary>
        public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

        /// <summary>
        /// How often the consumer needs to poll for new messages to be considered alive, polling greater than this interval triggers a rebalance
        /// Uses Kafka default of 300000
        /// </summary>
        public int MaxPollIntervalMs { get; set; } = 300000;


        /// <summary>
        /// How many partitions on this topic?
        /// </summary>
        public int NumPartitions { get; set; } = 1;

        /// <summary>
        /// What do we do if there is no offset stored in ZooKeeper for this consumer
        /// AutoOffsetReset.Earlist -  (default) Begin reading the stream from the start
        /// AutoOffsetReset.Latest - Start from now i.e. only consume messages after we start
        /// AutoOffsetReset.Error - Consider it an error to be lacking a reset
        /// </summary>
        public AutoOffsetReset OffsetDefault { get; set; } = AutoOffsetReset.Earliest;

        /// <summary>
        /// How long before we time out when we are reading the committed offsets back (mainly used for debugging)
        /// </summary>
        public int ReadCommittedOffsetsTimeOutMs { get; set; } = 5000;

        /// <summary>
        /// What is the replication factor? How many nodes is the topic copied to on the broker?
        /// </summary>
        public short ReplicationFactor { get; set; } = 1;

        /// <summary>
        /// If Kafka does not receive a heartbeat from the consumer within this time window, trigger a re-balance
        /// Default is Kafka default of 10s
        /// </summary>
        public int SessionTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// 
        /// </summary>
        public int SweepUncommittedOffsetsIntervalMs { get; set; } = 30000;

        /// <summary>
        /// How long to wait when asking for topic metadata
        /// </summary>
        public int TopicFindTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Initializes a new instance of the <see cref="Subscription"/> class.
        /// </summary>
        /// <param name="dataType">Type of the data.</param>
        /// <param name="name">The name. Defaults to the data type's full name.</param>
        /// <param name="channelName">The channel name. Defaults to the data type's full name.</param>
        /// <param name="routingKey">The routing key. Defaults to the data type's full name.</param>
        /// <param name="groupId">What is the id of the consumer group that this consumer belongs to; will not process the same partition as others in group</param>
        /// <param name="bufferSize">The number of messages to buffer at any one time, also the number of messages to retrieve at once. Min of 1 Max of 10</param>
        /// <param name="noOfPerformers">The no of threads reading this channel.</param>
        /// <param name="timeoutInMs">The timeout in milliseconds.</param>
        /// <param name="pollDelayInMs">Interval between polling attempts</param>
        /// <param name="noWorkPauseInMs">When a queue is empty, delay this long before re-reading from the queue</param>
        /// <param name="deferRoutingKey">In response to a DeferMessageAction exception, route to this topic</param>
        /// <param name="requeueCount">The number of times you want to requeue a message before dropping it.</param>
        /// <param name="requeueDelayInMs">The number of milliseconds to delay the delivery of a requeue message for.</param>
        /// <param name="deadLetterKey">If we hit the requeue limit, copy the message to this topic</param>
        /// <param name="unacceptableMessageLimit">The number of unacceptable messages to handle, before stopping reading from the channel.</param>
        /// <param name="offsetDefault">Where should we begin processing if we cannot find a stored offset</param>
        /// <param name="commitBatchSize">How often should we commit offsets?</param>
        /// <param name="sessionTimeoutMs">What is the heartbeat interval for this consumer, after which Kafka will assume dead and rebalance the consumer group</param>
        /// <param name="maxPollIntervalMs">How often does the consumer poll for a message to be considered alive, after which Kafka will assume dead and rebalance</param>
        /// <param name="sweepUncommittedOffsetsIntervalMs">How often do we commit offsets that have yet to be saved</param>
        /// <param name="isolationLevel">Should we read messages that are not on all replicas? May cause duplicates.</param>
        /// <param name="isAsync">Is this channel read asynchronously</param>
        /// <param name="numOfPartitions">How many partitions should this topic have - used if we create the topic</param>
        /// <param name="replicationFactor">How many copies of each partition should we have across our broker's nodes - used if we create the topic</param>       /// <param name="channelFactory">The channel factory to create channels for Consumer.</param>
        /// <param name="makeChannels">Should we make channels if they don't exist, defaults to creating</param>
        public KafkaSubscription(
            Type dataType,
            SubscriptionName name = null,
            ChannelName channelName = null,
            RoutingKey routingKey = null,
            string groupId = null,
            int bufferSize = 1,
            int noOfPerformers = 1,
            int timeoutInMs = 300,
            int pollDelayInMs = -1,
            int noWorkPauseInMs = 500,
            RoutingKey deferRoutingKey = null,
            int requeueCount = -1,
            int requeueDelayInMs = 0,
            RoutingKey deadLetterKey = null,
            int unacceptableMessageLimit = 0,
            AutoOffsetReset offsetDefault = AutoOffsetReset.Earliest,
            long commitBatchSize = 10,
            int sessionTimeoutMs = 10000,
            int maxPollIntervalMs = 300000,
            int sweepUncommittedOffsetsIntervalMs = 30000,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            bool isAsync = false,
            int numOfPartitions = 1,
            short replicationFactor = 1,
            IAmAChannelFactory channelFactory = null,
            OnMissingChannel makeChannels = OnMissingChannel.Create)
            : base(dataType, name, channelName, routingKey, bufferSize, noOfPerformers, timeoutInMs, pollDelayInMs,
                noWorkPauseInMs, requeueCount, requeueDelayInMs, unacceptableMessageLimit, isAsync, channelFactory, makeChannels)
        {
            CommitBatchSize = commitBatchSize;
            GroupId = groupId;
            IsolationLevel = isolationLevel;
            MaxPollIntervalMs = maxPollIntervalMs;
            SweepUncommittedOffsetsIntervalMs = sweepUncommittedOffsetsIntervalMs;
            OffsetDefault = offsetDefault;
            SessionTimeoutMs = sessionTimeoutMs;
            NumPartitions = numOfPartitions;
            ReplicationFactor = replicationFactor;
            DeferQueueTopic = deferRoutingKey;
            DeadLetterQueue = deadLetterKey;

            if (routingKey != null && DeferQueueTopic == null) DeferQueueTopic = new RoutingKey($"{routingKey.Value}_Defer_{requeueDelayInMs.ToString()}");
            if (routingKey != null && DeadLetterQueue == null) DeadLetterQueue = new RoutingKey($"{routingKey.Value}_DLQ");
        }
    }

    public class KafkaSubscription<T> : KafkaSubscription where T : IRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Subscription"/> class.
        /// </summary>
        /// <param name="name">The name. Defaults to the data type's full name.</param>
        /// <param name="channelName">The channel name. Defaults to the data type's full name.</param>
        /// <param name="routingKey">The routing key. Defaults to the data type's full name.</param>
        /// <param name="groupId">What is the id of the consumer group that this consumer belongs to; will not process the same partition as others in group</param>
        /// <param name="bufferSize">The number of messages to buffer at any one time, also the number of messages to retrieve at once. Min of 1 Max of 10</param>
        /// <param name="noOfPerformers">The no of threads reading this channel.</param>
        /// <param name="timeoutInMs">The timeout in milliseconds.</param>
        /// <param name="pollDelayInMs">Interval between polling attempts</param>
        /// <param name="noWorkPauseInMs">When a queue is empty, delay this long before re-reading from the queue</param>
        /// <param name="deferRoutingKey">In response to a DeferMessageAction exception, route to this topic</param>
        /// <param name="requeueCount">The number of times you want to requeue a message before dropping it.</param>
        /// <param name="requeueDelayInMs">The number of milliseconds to delay the delivery of a requeue message for.</param>
        /// <param name="deadLetterKey">If we hit the requeue limit, copy the message to this topic</param>
        /// <param name="unacceptableMessageLimit">The number of unacceptable messages to handle, before stopping reading from the channel.</param>
        /// <param name="offsetDefault">Where should we begin processing if we cannot find a stored offset</param>
        /// <param name="commitBatchSize">How often should we commit offsets?</param>
        /// <param name="sessionTimeoutMs">What is the heartbeat interval for this consumer, after which Kafka will assume dead and rebalance the consumer group</param>
        /// <param name="maxPollIntervalMs">How often does the consumer poll for a message to be considered alive, after which Kafka will assume dead and rebalance</param>
        /// <param name="sweepUncommittedOffsetsIntervalMs">How often do we commit offsets that have yet to be saved</param>
        /// <param name="isolationLevel">Should we read messages that are not on all replicas? May cause duplicates.</param>
        /// <param name="isAsync">Is this channel read asynchronously</param>
        /// <param name="numOfPartitions">How many partitions should this topic have - used if we create the topic</param>
        /// <param name="replicationFactor">How many copies of each partition should we have across our broker's nodes - used if we create the topic</param>
        /// <param name="deferQueueTopic"></param>
        /// <param name="deadLetterQueue"></param>
        /// <param name="makeChannels">Should we make channels if they don't exist, defaults to creating</param>
        /// <param name="channelFactory">The channel factory to create channels for Consumer.</param>
        public KafkaSubscription(
            SubscriptionName name = null,
            ChannelName channelName = null,
            RoutingKey routingKey = null,
            string groupId = null,
            int bufferSize = 1,
            int noOfPerformers = 1,
            int timeoutInMs = 300,
            int pollDelayInMs = -1,
            int noWorkPauseInMs = 500,
            RoutingKey deferRoutingKey = null,
            int requeueCount = -1,
            int requeueDelayInMs = 0,
            RoutingKey deadLetterKey = null,
            int unacceptableMessageLimit = 0,
            AutoOffsetReset offsetDefault = AutoOffsetReset.Earliest,
            long commitBatchSize = 10,
            int sessionTimeoutMs = 10000,
            int maxPollIntervalMs = 300000,
            int sweepUncommittedOffsetsIntervalMs = 30000,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            bool isAsync = false,
            int numOfPartitions = 1,
            short replicationFactor = 1,
            IAmAChannelFactory channelFactory = null,
            OnMissingChannel makeChannels = OnMissingChannel.Create)
            : base(typeof(T), name, channelName, routingKey, groupId, bufferSize, noOfPerformers, timeoutInMs, pollDelayInMs, noWorkPauseInMs,
                deferRoutingKey, requeueCount, requeueDelayInMs, deadLetterKey, unacceptableMessageLimit, offsetDefault, commitBatchSize, sessionTimeoutMs,
                maxPollIntervalMs, sweepUncommittedOffsetsIntervalMs, isolationLevel, isAsync, numOfPartitions, replicationFactor,
                channelFactory, makeChannels)
        {
        }
    }
}
