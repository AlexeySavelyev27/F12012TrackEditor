#include "PSSGParser.h"
#include <fstream>
#include <vector>
#include <zlib.h>
#include <stdexcept>
#include <cstring>

static uint32_t read_u32(std::istream &s) {
    unsigned char buf[4];
    s.read(reinterpret_cast<char*>(buf), 4);
    return (buf[0]<<24)|(buf[1]<<16)|(buf[2]<<8)|buf[3];
}

PSSGParser::PSSGParser(const std::string &p): path(p) {}

PSSGNode PSSGParser::parse() {
    std::ifstream f(path, std::ios::binary);
    if(!f) throw std::runtime_error("Unable to open file");
    std::vector<unsigned char> data((std::istreambuf_iterator<char>(f)), {});
    if(data.size()>=2 && data[0]==0x1f && data[1]==0x8b) {
        unsigned long dst_len = data.size()*4; // crude
        std::vector<unsigned char> out(dst_len);
        if(uncompress(out.data(), &dst_len, data.data(), data.size())!=Z_OK)
            throw std::runtime_error("gzip decompress failed");
        out.resize(dst_len);
        data.swap(out);
    }
    std::istringstream s(std::string(data.begin(), data.end()));
    char sig[4];
    s.read(sig,4);
    if(std::strncmp(sig,"PSSG",4)!=0) throw std::runtime_error("not PSSG");
    read_u32(s); // file length
    // schema parsing omitted (simplified)
    PSSGNode root{"ROOT"};
    // TODO parse schema and nodes fully
    return root;
}
