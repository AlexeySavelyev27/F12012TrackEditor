#include "PSSGParser.h"
#include "PSSGSchema.h"
#include <fstream>
#include <stdexcept>
#include <zlib.h>
#include <algorithm>

PSSGParser::PSSGParser(const std::string& path) : path_(path) {}

static uint32_t ReadBE32(const unsigned char* buf) {
    return (static_cast<uint32_t>(buf[0]) << 24) |
           (static_cast<uint32_t>(buf[1]) << 16) |
           (static_cast<uint32_t>(buf[2]) << 8) |
           (static_cast<uint32_t>(buf[3]));
}

PSSGNode PSSGParser::Parse() {
    std::ifstream f(path_, std::ios::binary);
    if(!f) throw std::runtime_error("Cannot open file");
    unsigned char magic[2];
    f.read(reinterpret_cast<char*>(magic), 2);
    f.seekg(0);
    if(magic[0]==0x1f && magic[1]==0x8b) {
        gzFile g = gzopen(path_.c_str(), "rb");
        if(!g) throw std::runtime_error("gzopen failed");
        char buf[4096];
        int n;
        while((n = gzread(g, buf, sizeof(buf))) > 0) {
            data_.insert(data_.end(), buf, buf+n);
        }
        gzclose(g);
    } else {
        f.seekg(0, std::ios::end);
        size_t size = f.tellg();
        f.seekg(0);
        data_.resize(size);
        f.read(reinterpret_cast<char*>(data_.data()), size);
    }
    pos_ = 0;

    auto sig = ReadBytes(4);
    if(std::string(sig.begin(), sig.end()) != "PSSG") {
        throw std::runtime_error("Not a PSSG file");
    }
    uint32_t file_len = ReadUInt();
    (void)file_len;

    uint32_t attr_info_count = ReadUInt();
    uint32_t node_info_count = ReadUInt();
    for(uint32_t i=0;i<node_info_count;++i) {
        uint32_t node_id = ReadUInt();
        uint32_t name_len = ReadUInt();
        auto name_bytes = ReadBytes(name_len);
        std::string name(name_bytes.begin(), name_bytes.end());
        uint32_t attr_count = ReadUInt();
        schema_.node_id_to_name[node_id] = name;
        schema_.node_name_to_id[name] = node_id;
        auto& amap = schema_.attr_id_to_name[node_id];
        auto& rmap = schema_.attr_name_to_id[name];
        for(uint32_t a=0;a<attr_count;++a) {
            uint32_t attr_id = ReadUInt();
            uint32_t attr_len = ReadUInt();
            auto attr_name_b = ReadBytes(attr_len);
            std::string attr_name(attr_name_b.begin(), attr_name_b.end());
            amap[attr_id] = attr_name;
            rmap[attr_name] = attr_id;
        }
    }

    return ReadNode();
}

PSSGNode PSSGParser::ReadNode() {
    uint32_t node_id = ReadUInt();
    uint32_t node_size = ReadUInt();
    uint32_t node_end = static_cast<uint32_t>(pos_) + node_size;
    uint32_t attr_block_size = ReadUInt();
    uint32_t attr_end = static_cast<uint32_t>(pos_) + attr_block_size;

    PSSGNode node;
    auto itName = schema_.node_id_to_name.find(node_id);
    node.name = itName!=schema_.node_id_to_name.end()? itName->second : "unknown";

    while(pos_ < attr_end) {
        uint32_t attr_id = ReadUInt();
        uint32_t val_size = ReadUInt();
        auto val = ReadBytes(val_size);
        std::string attr_name = schema_.attr_id_to_name[node_id][attr_id];
        node.attributes[attr_name] = std::move(val);
    }
    while(pos_ < node_end) {
        if(node_end - pos_ >= 12) {
            uint32_t peek = ReadBE32(&data_[pos_]);
            if(schema_.node_id_to_name.count(peek)) {
                PSSGNode child = ReadNode();
                node.children.push_back(std::move(child));
                continue;
            }
        }
        auto remaining = node_end - pos_;
        node.data = ReadBytes(remaining);
        break;
    }
    pos_ = node_end;
    return node;
}

uint32_t PSSGParser::ReadUInt() {
    Ensure(4);
    uint32_t v = ReadBE32(&data_[pos_]);
    pos_ += 4;
    return v;
}

std::vector<unsigned char> PSSGParser::ReadBytes(size_t count) {
    Ensure(count);
    std::vector<unsigned char> out(data_.begin()+pos_, data_.begin()+pos_+count);
    pos_ += count;
    return out;
}

void PSSGParser::Ensure(size_t count) {
    if(pos_ + count > data_.size()) throw std::runtime_error("unexpected EOF");
}
