// Copyright 2016-2017 Confluent Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Refer to LICENSE for more information.

#pragma warning disable xUnit1026

using System;
using System.Text;
using System.Collections.Generic;
using Xunit;
using Confluent.Kafka.Serialization;


namespace Confluent.Kafka.IntegrationTests
{
    public static partial class Tests
    {
        class DeliveryHandler_MHPC : IDeliveryHandler<Null, string>
        {
            public static List<Message<Null, string>> drs 
                = new List<Message<Null, string>>();

            public bool MarshalData 
                => true;

            public void HandleDeliveryReport(Message<Null, string> message)
            {
                drs.Add(message);
            }
        }

        class DeliveryHandler_MHPC_2 : IDeliveryHandler
        {
            public static List<Message> drs 
                = new List<Message>();

            public bool MarshalData 
                => true;

            public void HandleDeliveryReport(Message message)
            {
                drs.Add(message);
            }
        }

        /// <summary>
        ///     Test various message header produce / consume scenarios.
        /// </summary>
        [Theory, MemberData(nameof(KafkaParameters))]
        public static void MessageHeaderProduceConsume(string bootstrapServers, string singlePartitionTopic, string partitionedTopic)
        {
            var producerConfig = new Dictionary<string, object>
            {
                { "bootstrap.servers", bootstrapServers }
            };

            var consumerConfig = new Dictionary<string, object>
            {
                { "group.id", Guid.NewGuid().ToString() },
                { "bootstrap.servers", bootstrapServers },
                { "session.timeout.ms", 6000 }
            };

            Message<Null, string> dr_single, dr_empty, dr_null, dr_multiple, dr_duplicate;
            Message<Null, string> dr_ol1, dr_ol2, dr_ol3;
            using (var producer = new Producer<Null, string>(producerConfig, null, new StringSerializer(Encoding.UTF8)))
            {
                // single header value.
                var headers = new Dictionary<string, byte[]>();
                headers.Add("test-header", new byte[] { 142 } );
                dr_single = producer.ProduceAsync(singlePartitionTopic, Partition.NotSpecified, null, "the value", Timestamp.Default, headers).Result;

                // empty header values
                var headers0 = new Dictionary<string, byte[]>();
                dr_empty = producer.ProduceAsync(singlePartitionTopic, Partition.NotSpecified, null, "the value", Timestamp.Default, headers0).Result;

                // null header value
                dr_null = producer.ProduceAsync(singlePartitionTopic, Partition.NotSpecified, null, "the value", Timestamp.Default, null).Result;

                // multiple header values (also Headers no Dictionary, since order is tested).
                var headers2 = new Headers();
                headers2.Add("test-header-a", new byte[] { 111 } );
                headers2.Add("test-header-b", new byte[] { 112 } );
                dr_multiple = producer.ProduceAsync(singlePartitionTopic, Partition.NotSpecified, null, "the value", Timestamp.Default, headers2).Result;

                // duplicate header values (also List not Dictionary)
                var headers3 = new List<KeyValuePair<string, byte[]>>();
                headers3.Add(new KeyValuePair<string, byte[]>("test-header-a", new byte[] { 111 } ));
                headers3.Add(new KeyValuePair<string, byte[]>("test-header-b", new byte[] { 112 } ));
                headers3.Add(new KeyValuePair<string, byte[]>("test-header-a", new byte[] { 113 } ));
                headers3.Add(new KeyValuePair<string, byte[]>("test-header-b", new byte[] { 114 } ));
                headers3.Add(new KeyValuePair<string, byte[]>("test-header-c", new byte[] { 115 } ));
                dr_duplicate = producer.ProduceAsync(singlePartitionTopic, Partition.NotSpecified, null, "the value", Timestamp.Default, headers3).Result;

                // Test headers work as expected with all serializing ProduceAsync variants. 

                dr_ol1 = producer.ProduceAsync(singlePartitionTopic, null, "the value").Result;
                dr_ol2 = producer.ProduceAsync(new Message<Null, string>(singlePartitionTopic, 0, 0, null, "the value", Timestamp.Default, headers2, null)).Result;
                dr_ol3 = producer.ProduceAsync(singlePartitionTopic, 0, null, "the value", Timestamp.Default, headers).Result;

                var dh = new DeliveryHandler_MHPC();

                // Test headers work as expected with all serializing Produce variants. 

                // TODO: Consider not requiring IDeliveryHandler - a simple delegate would be easier.
                producer.Produce(singlePartitionTopic, null, "the value", dh);
                producer.Produce(new Message<Null, string>(singlePartitionTopic, 0, 0, null, "the value", Timestamp.Default, headers2, null), dh);
                producer.Produce(singlePartitionTopic, 0, null, "the value", Timestamp.Default, headers, dh);

                producer.Flush(TimeSpan.FromSeconds(30));
            }

            Message dr_ol4, dr_ol5, dr_ol6, dr_ol7;
            using (var producer = new Producer(producerConfig))
            {
                var headers = new Headers();
                headers.Add("hkey", new byte[] { 44 });

                // Test headers work as expected with all non-serializing ProduceAsync variants. 

                dr_ol4 = producer.ProduceAsync(new Message(singlePartitionTopic, 0, Offset.Invalid, null, null, Timestamp.Default, headers, null)).Result;
                dr_ol5 = producer.ProduceAsync(singlePartitionTopic, null, null).Result;
                dr_ol6 = producer.ProduceAsync(singlePartitionTopic, Partition.NotSpecified, null, null, Timestamp.Default, headers).Result;
                dr_ol7 = producer.ProduceAsync(singlePartitionTopic, Partition.NotSpecified, null, 0, 0, null, 0, 0, Timestamp.Default, headers).Result;

                // Test headers work as expected with all non-serializing Produce variants.

                var dh = new DeliveryHandler_MHPC_2();
                producer.Produce(new Message(singlePartitionTopic, 0, Offset.Invalid, null, null, Timestamp.Default, headers, null), dh);
                producer.Produce(singlePartitionTopic, null, null, dh);
                producer.Produce(singlePartitionTopic, Partition.NotSpecified, null, null, Timestamp.Default, headers, dh);
                producer.Produce(singlePartitionTopic, Partition.NotSpecified, null, 0, 0, null, 0, 0, Timestamp.Default, headers, dh);

                producer.Flush(TimeSpan.FromSeconds(10));
            }

            using (var consumer = new Consumer(consumerConfig))
            {
                consumer.Assign(new List<TopicPartitionOffset>() {dr_single.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg, TimeSpan.FromSeconds(10)));
                Assert.Single(msg.Headers);
                Assert.Equal("test-header", msg.Headers[0].Key);
                Assert.Equal(new byte[] { 142 }, msg.Headers[0].Value);

                consumer.Assign(new List<TopicPartitionOffset>() {dr_empty.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg2, TimeSpan.FromSeconds(10)));
                // following Java, alway instantiate a new Headers instance, even in the empty case.
                Assert.NotNull(msg2.Headers);
                Assert.Empty(msg2.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {dr_null.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg3, TimeSpan.FromSeconds(10)));
                Assert.NotNull(msg3.Headers);
                Assert.Empty(msg3.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {dr_multiple.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg4, TimeSpan.FromSeconds(10)));
                Assert.Equal(2, msg4.Headers.Count);
                Assert.Equal("test-header-a", msg4.Headers[0].Key);
                Assert.Equal("test-header-b", msg4.Headers[1].Key);
                Assert.Equal(new byte[] { 111 }, msg4.Headers[0].Value);
                Assert.Equal(new byte[] { 112 }, msg4.Headers[1].Value);

                consumer.Assign(new List<TopicPartitionOffset>() {dr_duplicate.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg5, TimeSpan.FromSeconds(10)));
                Assert.Equal(5, msg5.Headers.Count);
                Assert.Equal("test-header-a", msg5.Headers[0].Key);
                Assert.Equal("test-header-b", msg5.Headers[1].Key);
                Assert.Equal("test-header-a", msg5.Headers[2].Key);
                Assert.Equal("test-header-b", msg5.Headers[3].Key);
                Assert.Equal("test-header-c", msg5.Headers[4].Key);
                Assert.Equal(new byte[] { 111 }, msg5.Headers[0].Value);
                Assert.Equal(new byte[] { 112 }, msg5.Headers[1].Value);
                Assert.Equal(new byte[] { 113 }, msg5.Headers[2].Value);
                Assert.Equal(new byte[] { 114 }, msg5.Headers[3].Value);
                Assert.Equal(new byte[] { 115 }, msg5.Headers[4].Value);
                Assert.Equal(new byte[] { 113 }, msg5.Headers.GetLast("test-header-a"));
                Assert.Equal(new byte[] { 114 }, msg5.Headers.GetLast("test-header-b"));
                Assert.Equal(new byte[] { 115 }, msg5.Headers.GetLast("test-header-c"));

                // Test headers work with all produce method variants.

                // async, serializing
                consumer.Assign(new List<TopicPartitionOffset>() {dr_ol1.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg6, TimeSpan.FromSeconds(10)));
                Assert.Empty(msg6.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {dr_ol2.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg7, TimeSpan.FromSeconds(10)));
                Assert.Equal(2, msg7.Headers.Count);

                consumer.Assign(new List<TopicPartitionOffset>() {dr_ol3.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg8, TimeSpan.FromSeconds(10)));
                Assert.Single(msg8.Headers);

                // delivery-handler, serializing.
                consumer.Assign(new List<TopicPartitionOffset>() {DeliveryHandler_MHPC.drs[0].TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg9, TimeSpan.FromSeconds(10)));
                Assert.Empty(msg9.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {DeliveryHandler_MHPC.drs[1].TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg10, TimeSpan.FromSeconds(10)));
                Assert.Equal(2, msg10.Headers.Count);

                consumer.Assign(new List<TopicPartitionOffset>() {DeliveryHandler_MHPC.drs[2].TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg11, TimeSpan.FromSeconds(10)));
                Assert.Single(msg11.Headers);

                // async, non-serializing
                consumer.Assign(new List<TopicPartitionOffset>() {dr_ol4.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg12, TimeSpan.FromSeconds(10)));
                Assert.Single(msg12.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {dr_ol5.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg13, TimeSpan.FromSeconds(10)));
                Assert.Empty(msg13.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {dr_ol6.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg14, TimeSpan.FromSeconds(10)));
                Assert.Single(msg14.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {dr_ol7.TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg15, TimeSpan.FromSeconds(10)));
                Assert.Single(msg15.Headers);

                // delivery handler, non-serializing
                consumer.Assign(new List<TopicPartitionOffset>() {DeliveryHandler_MHPC_2.drs[0].TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg16, TimeSpan.FromSeconds(10)));
                Assert.Single(msg16.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {DeliveryHandler_MHPC_2.drs[1].TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg17, TimeSpan.FromSeconds(10)));
                Assert.Empty(msg17.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {DeliveryHandler_MHPC_2.drs[2].TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg18, TimeSpan.FromSeconds(10)));
                Assert.Single(msg18.Headers);

                consumer.Assign(new List<TopicPartitionOffset>() {DeliveryHandler_MHPC_2.drs[3].TopicPartitionOffset});
                Assert.True(consumer.Consume(out Message msg19, TimeSpan.FromSeconds(10)));
                Assert.Single(msg19.Headers);
            }
        }
    }
}
