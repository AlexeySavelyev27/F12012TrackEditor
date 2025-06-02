#include "PSSGWriter.h"
#include <fstream>
#include <cstring>

static void WriteUInt(std::ostream& stream, uint32_t val) {
    unsigned char buf[4];
    buf[0] = (val >> 24) & 0xFF;
    buf[1] = (val >> 16) & 0xFF;
    buf[2] = (val >> 8) & 0xFF;
    buf[3] = val & 0xFF;
    stream.write(reinterpret_cast<char*>(buf), 4);
}

PSSGWriter::PSSGWriter(const PSSGNode& root) : root_(root) {}

void PSSGWriter::Save(const std::string& path) {
    schema_.BuildFromTree(root_);
    ComputeSizes(root_);
    std::ofstream file(path, std::ios::binary);
    if(!file) return;

    file.write("PSSG", 4);
    WriteUInt(file, 0); // placeholder length

    uint32_t attr_entry_count = 0;
    for(const auto& kv : schema_.attr_name_to_id) attr_entry_count += kv.second.size();
    WriteUInt(file, attr_entry_count);
    WriteUInt(file, static_cast<uint32_t>(schema_.node_name_to_id.size()));
    for(const auto& kv : schema_.node_name_to_id) {
        WriteUInt(file, kv.second);
        WriteUInt(file, static_cast<uint32_t>(kv.first.size()));
        file.write(kv.first.data(), kv.first.size());
        const auto& amap = schema_.attr_name_to_id[kv.first];
        WriteUInt(file, static_cast<uint32_t>(amap.size()));
        for(const auto& av : amap) {
            WriteUInt(file, av.second);
            WriteUInt(file, static_cast<uint32_t>(av.first.size()));
            file.write(av.first.data(), av.first.size());
        }
    }

    WriteNode(file, root_);

    std::streampos end = file.tellp();
    file.seekp(4);
    WriteUInt(file, static_cast<uint32_t>(end - 8));
}

void PSSGWriter::WriteNode(std::ostream& stream, const PSSGNode& node) {
    uint32_t node_id = schema_.node_name_to_id[node.name];
    WriteUInt(stream, node_id);
    WriteUInt(stream, node.node_size);
    WriteUInt(stream, node.attr_block_size);
    for(const auto& kv : node.attributes) {
        uint32_t attr_id = schema_.attr_name_to_id[node.name][kv.first];
        WriteUInt(stream, attr_id);
        WriteUInt(stream, static_cast<uint32_t>(kv.second.size()));
        stream.write(reinterpret_cast<const char*>(kv.second.data()), kv.second.size());
    }
    if(!node.children.empty()) {
        for(const auto& c : node.children) {
            WriteNode(stream, c);
        }
    } else {
        if(!node.data.empty()) {
            stream.write(reinterpret_cast<const char*>(node.data.data()), node.data.size());
        }
    }
}

void PSSGWriter::ComputeSizes(PSSGNode& node) {
    uint32_t attr_size = 0;
    for(const auto& kv : node.attributes) {
        attr_size += 8 + static_cast<uint32_t>(kv.second.size());
    }
    uint32_t child_payload = 0;
    if(!node.children.empty()) {
        for(auto& c : node.children) {
            ComputeSizes(c);
            child_payload += 8 + c.node_size;
        }
    } else {
        child_payload = static_cast<uint32_t>(node.data.size());
    }
    node.attr_block_size = attr_size;
    node.node_size = 4 + attr_size + child_payload;
}
