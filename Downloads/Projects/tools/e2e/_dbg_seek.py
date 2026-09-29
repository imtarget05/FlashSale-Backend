#!/usr/bin/env python3
"""Debug 2: tái hiện đúng pattern của eventbus (subscribe, không assign).

Hỏi: sau khi handler fail và seek(0), message có được giao lại không?
"""
import json
import time

from confluent_kafka import (OFFSET_BEGINNING, Consumer, Producer, TopicPartition)
from confluent_kafka.admin import AdminClient, NewTopic

BS = "localhost:9092"
topic = f"dbg2-{int(time.time())}"

adm = AdminClient({"bootstrap.servers": BS})
adm.create_topics([NewTopic(topic, 1, 1)])
time.sleep(2)

p = Producer({"bootstrap.servers": BS})
p.produce(topic, value=json.dumps({"event_id": "poison"}))
p.produce(topic, value=json.dumps({"event_id": "healthy"}))
p.flush(10)

c = Consumer({"bootstrap.servers": BS, "group.id": f"dbg2-{int(time.time())}",
              "auto.offset.reset": "earliest", "enable.auto.commit": False})
c.subscribe([topic])

# y hệt eventbus
dl = time.monotonic() + 15.0
while not c.assignment() and time.monotonic() < dl:
    c.poll(0.2)
for tp in c.assignment() or []:
    c.seek(TopicPartition(tp.topic, tp.partition, OFFSET_BEGINNING))
print(f"assignment={c.assignment()}", flush=True)

attempts = 0
for k in range(6):
    m = c.poll(3.0)
    if m is None:
        print(f"poll {k}: None", flush=True)
        continue
    payload = json.loads(m.value().decode())
    eid = payload["event_id"]
    print(f"poll {k}: offset={m.offset()} id={eid}", flush=True)
    if eid == "poison":
        attempts += 1
        print(f"   attempt {attempts} -> seek({m.offset()})", flush=True)
        c.seek(TopicPartition(topic, m.partition(), m.offset()))
    else:
        c.commit(m, asynchronous=False)
        print("   committed healthy", flush=True)

print(f"\ntotal poison attempts = {attempts}")
c.close()

