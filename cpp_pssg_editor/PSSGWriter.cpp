#include "PSSGWriter.h"
#include <fstream>
#include <stdexcept>
#include <cstring>

static void write_u32(std::ofstream &s, uint32_t v) {
    unsigned char buf[4];
    buf[0]=v>>24; buf[1]=v>>16; buf[2]=v>>8; buf[3]=v;
    s.write(reinterpret_cast<char*>(buf),4);
}

PSSGWriter::PSSGWriter(const PSSGNode &r): root(r) {}

void PSSGWriter::computeSizes(PSSGNode &node) {
    unsigned int attrSize = 0;
    for(const auto &p: node.attributes) attrSize += 8 + p.second.size();
    unsigned int payload = 0;
    if(!node.children.empty()) {
        for(auto &c: node.children) { computeSizes(c); payload += 8 + c.nodeSize; }
    } else {
        payload = node.data.size();
    }
    node.attrBlockSize = attrSize;
    node.nodeSize = 4 + attrSize + payload;
}

void PSSGWriter::save(const std::string &path) {
    PSSGNode copy = root;
    computeSizes(copy);
    std::ofstream out(path, std::ios::binary);
    out.write("PSSG",4);
    write_u32(out,0); // length later
    // schema writing omitted (simplified)
    // TODO: write node tree
    out.seekp(4);
    write_u32(out, (uint32_t)(out.tellp()) - 8);
}

void PSSGWriter::writeNode(std::ofstream &out, const PSSGNode &node) {
    // TODO implement
}
